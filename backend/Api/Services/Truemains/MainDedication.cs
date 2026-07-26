using Core.Lol.Map;
using Core.Truemains;
using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

/// <summary>
/// Loads the inputs of the dedication score and turns them into the read model
/// every truemain surface shows. Single source of truth for how a player's
/// <em>signature champion</em> is picked and how its history is measured, so the
/// profile card and the leaderboard column can't drift apart — the same role
/// <see cref="MainPositions"/> plays for lanes.
/// </summary>
/// <remarks>
/// <para>
/// The signature champion is the player's most-played main (PlayRate desc, then
/// ChampionMatches desc — the same order the profile and the leaderboard use to
/// pick a player's top champion). <b>Only the champion filter re-points it</b>:
/// the leaderboard filtered to Yasuo must rank Yasuo dedication, not each
/// player's unrelated top main.
/// </para>
/// <para>
/// Every other leaderboard filter — <c>position</c>, <c>otpOnly</c>, the
/// ranked-games floor — decides which players are <em>eligible</em> and never
/// which champion they are scored on. A lane filter means "show me players who
/// play this lane", so a top-laner who also mains a mid champion still shows
/// their top-lane score under <c>?position=MIDDLE</c>. That keeps the dedication
/// cell about the same champion as the row's leading champion icon (which is
/// likewise position-blind), and keeps the leaderboard column equal to the
/// profile card for the same player. Letting a lane filter reach the pick is
/// exactly what once made the score change when the sort was toggled.
/// </para>
/// <para>
/// Play rate comes from <c>main_champion_stats</c> (main analysis' rolling
/// window over the account's recent ranked games); games / patches / last-played
/// come from <c>champion_aggregate_scopes</c>, which is the only durable source
/// for a player's career on a champion — <c>match_participants</c> is
/// hard-deleted by retention beyond the last couple of patches, while old-patch
/// scopes stay frozen (#466). A player whose scopes haven't been built yet
/// simply scores on commitment alone.
/// </para>
/// <para>
/// Everything is computed at read time. Nothing here is materialised: the score
/// changes whenever a game is ingested or a patch ships, and the inputs are a
/// handful of indexed columns, so a per-request read stays cheaper than keeping
/// a denormalised column honest.
/// </para>
/// </remarks>
internal static class MainDedication
{
    // Ranked solo queue — the same queue main analysis classifies mains on, so
    // the career totals below count the games the play rate was measured over.
    private const int RankedQueueId = (int)LolQueueId.RankedSoloDuo;

    /// <summary>
    /// Dedication for a known set of accounts (a leaderboard page slice, a
    /// dedication-ranked candidate set, or a single profile). Accounts with no
    /// classified main are absent from the result — the caller renders nothing
    /// rather than a zero.
    /// </summary>
    /// <remarks>
    /// The single entry point for scoring: every surface funnels through here, so
    /// the signature champion for a given (account, championId) pair is the same
    /// whatever the caller. Note the parameter list — there is deliberately no
    /// position or otpOnly argument, because neither may influence the pick.
    /// </remarks>
    /// <param name="ctx">Context to run on. Callers hydrating concurrently must pass their own short-lived context (a single DbContext is not thread-safe).</param>
    /// <param name="accountIds">Accounts to score.</param>
    /// <param name="championId">When set, score this champion instead of each account's top main.</param>
    /// <param name="nowUtc">Clock reference for the recency decay.</param>
    /// <param name="ct">Request cancellation token.</param>
    public static async Task<Dictionary<Guid, DedicationReadModel>> FetchAsync(
        TrueMainDbContext ctx,
        Guid[] accountIds,
        int? championId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (accountIds.Length == 0)
        {
            return new Dictionary<Guid, DedicationReadModel>();
        }

        // DISTINCT ON collapses each account to one signature champion inside
        // the database, so the LATERAL below runs once per account rather than
        // once per main. The lateral is an index seek on the
        // (RiotAccountId, ChampionId, ...) unique index — cheap on a page slice.
        FormattableString sql = $"""
            WITH mains AS (
                SELECT DISTINCT ON (a."Id")
                    a."Id" AS "AccountId",
                    m."ChampionId" AS "ChampionId",
                    m."PlayRate" AS "PlayRate"
                FROM riot_accounts a
                JOIN main_champion_stats m
                  ON m."PlatformId" = a."PlatformId" AND m."Puuid" = a."Puuid"
                WHERE a."Id" = ANY ({accountIds})
                  AND m."IsMain" = true
                  AND m."IsActive" = true
                  AND ({championId}::int IS NULL OR m."ChampionId" = {championId})
                ORDER BY a."Id", m."PlayRate" DESC, m."ChampionMatches" DESC
            )
            SELECT
                mains."AccountId" AS "AccountId",
                mains."ChampionId" AS "ChampionId",
                mains."PlayRate" AS "PlayRate",
                COALESCE(career."CareerGames", 0) AS "CareerGames",
                COALESCE(career."PatchSpan", 0) AS "PatchSpan",
                career."LastGameUtc" AS "LastGameUtc"
            FROM mains
            LEFT JOIN LATERAL (
                SELECT
                    SUM(s."Games")::int AS "CareerGames",
                    COUNT(DISTINCT s."GameVersion")::int AS "PatchSpan",
                    MAX(s."LastGameStartTimeUtc") AS "LastGameUtc"
                FROM champion_aggregate_scopes s
                WHERE s."RiotAccountId" = mains."AccountId"
                  AND s."ChampionId" = mains."ChampionId"
                  AND s."QueueId" = {RankedQueueId}
            ) career ON TRUE
            """;

        var rows = await ctx.Database.SqlQuery<DedicationRow>(sql).ToListAsync(ct);
        return rows.ToDictionary(row => row.AccountId, row => Project(row, nowUtc));
    }

    /// <summary>
    /// Dedication for every account matching the leaderboard's filters, so the
    /// caller can rank on it. Ordering and pagination happen in memory on the
    /// projected scores — the score is a read-time expression, so there is no
    /// column to ORDER BY in SQL.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two phases, deliberately split: eligibility (which accounts the filters
    /// admit) then scoring (which champion each of them is scored on, and how it
    /// measures). The scoring phase is <see cref="FetchAsync"/> — literally the
    /// same call the rank-sorted leaderboard and the profile make — so the
    /// signature champion cannot depend on which sort is active. Folding the
    /// filters into the <c>DISTINCT ON</c> that picks the champion is what made
    /// <c>?position=X</c> score a different champion per sort; keeping the phases
    /// apart makes that class of drift structurally impossible.
    /// </para>
    /// <para>
    /// The candidate scan is capped at <paramref name="limit"/> accounts taken by
    /// descending play rate on their best matching main. Play rate carries the
    /// heaviest weight (<see cref="DedicationScore.CommitmentWeight"/>), so the
    /// rows the cap drops are the least committed of the population — the safety
    /// valve protects the API from an unbounded scan if the tracked population
    /// grows by orders of magnitude, at the cost of the deep tail of the ranking.
    /// </para>
    /// <para>
    /// One deliberate asymmetry remains, scoped to that truncation: under a
    /// position filter the play rate ordering the cap uses is the one of the main
    /// that <em>satisfies the filter</em>, which need not be the top main
    /// <see cref="FetchAsync"/> goes on to score. Ranking the accounts under the
    /// cap by their true top-main play rate would need a correlated MAX per
    /// candidate row — precisely the unbounded work the cap exists to avoid. So
    /// the cap may drop a marginally different set at the very tail; every
    /// account that survives it is still scored on its true signature champion,
    /// so this cannot affect a score, only which far-tail rows exist at all.
    /// </para>
    /// </remarks>
    public static async Task<DedicationCandidates> FetchCandidatesAsync(
        TrueMainDbContext ctx,
        string[] platforms,
        int? championId,
        string? position,
        int minGames,
        bool otpOnly,
        double minPositionShare,
        DateTime nowUtc,
        int limit,
        CancellationToken ct)
    {
        if (platforms.Length == 0 || limit <= 0)
        {
            return DedicationCandidates.Empty;
        }

        // Ask for one id past the cap. A result of exactly `limit` rows is
        // ambiguous on its own — a population sitting precisely at the cap looks
        // identical to one the LIMIT cut short — and the truncation flag is not
        // cosmetic: it gates the warning that tells us the score has outgrown a
        // read-time computation. A signal that can cry wolf is one nobody can
        // act on, so spend one row to make it exact. (The guard keeps
        // limit + 1 from overflowing; at int.MaxValue truncation is unreachable
        // anyway.)
        var probeLimit = limit < int.MaxValue ? limit + 1 : limit;
        var accountIds = await FetchEligibleAccountIdsAsync(
            ctx, platforms, championId, position, minGames, otpOnly, minPositionShare, probeLimit, ct);

        // Drop the probe row before anything else touches the set: it must not
        // reach the scoring lateral, the ranking or the page — it exists only to
        // answer "was there more?".
        var truncated = accountIds.Length > limit;
        if (truncated)
        {
            accountIds = accountIds[..limit];
        }

        if (accountIds.Length == 0)
        {
            return DedicationCandidates.Empty;
        }

        var byAccount = await FetchAsync(ctx, accountIds, championId, nowUtc, ct);

        var ranked = byAccount
            .Select(entry => new DedicationCandidate(entry.Key, entry.Value))
            // Descending score, then a deterministic tiebreak on the account id
            // so two genuinely tied players keep a stable order across requests
            // (otherwise page 2 could repeat or skip a row).
            .OrderByDescending(candidate => candidate.Dedication.Score)
            .ThenBy(candidate => candidate.AccountId)
            .ToList();

        return new DedicationCandidates(ranked, truncated);
    }

    /// <summary>
    /// The accounts the leaderboard's filters admit, one id per account.
    /// </summary>
    /// <remarks>
    /// The predicate must stay in lock-step with
    /// <c>TruemainsLeaderboardQueryService.CountAsync</c> — every filter lands on
    /// the same <c>main_champion_stats</c> row, so <c>?championId=X&amp;position=Y</c>
    /// means "has an X main played in Y" and the total agrees with the ranked
    /// slice. This is membership only: nothing selected here reaches the score,
    /// which is why the <c>DISTINCT ON</c> below projects a play rate (for the cap
    /// ordering) and no champion id.
    /// </remarks>
    private static async Task<Guid[]> FetchEligibleAccountIdsAsync(
        TrueMainDbContext ctx,
        string[] platforms,
        int? championId,
        string? position,
        int minGames,
        bool otpOnly,
        double minPositionShare,
        int limit,
        CancellationToken ct)
    {
        FormattableString sql = $"""
            WITH matching AS (
                SELECT DISTINCT ON (a."Id")
                    a."Id" AS "AccountId",
                    m."PlayRate" AS "PlayRate"
                FROM riot_accounts a
                JOIN main_champion_stats m
                  ON m."PlatformId" = a."PlatformId" AND m."Puuid" = a."Puuid"
                WHERE a."PlatformId" = ANY ({platforms})
                  AND a."Score" IS NOT NULL
                  AND m."IsMain" = true
                  AND m."IsActive" = true
                  AND m."TotalMatches" >= {minGames}
                  AND ({otpOnly}::bool = false OR m."IsOtp" = true)
                  AND ({championId}::int IS NULL OR m."ChampionId" = {championId})
                  AND ({position}::text IS NULL OR EXISTS (
                      SELECT 1
                      FROM jsonb_array_elements(m."PositionBreakdown") AS pos
                      WHERE pos->>'Position' = {position}
                        AND (pos->>'Rate')::float8 >= {minPositionShare}
                  ))
                ORDER BY a."Id", m."PlayRate" DESC, m."ChampionMatches" DESC
            )
            SELECT "AccountId" AS "Value"
            FROM matching
            ORDER BY "PlayRate" DESC, "AccountId"
            LIMIT {limit}
            """;

        var ids = await ctx.Database.SqlQuery<Guid>(sql).ToListAsync(ct);
        return [.. ids];
    }

    private static DedicationReadModel Project(DedicationRow row, DateTime nowUtc)
    {
        // No aggregated game yet (scopes not built for this account/champion):
        // treat recency as "infinitely old" rather than "played today", and
        // report a null day count so the UI can say "no tracked game" instead of
        // printing a fabricated 0.
        double? daysSinceLastGame = row.LastGameUtc is null
            ? null
            : Math.Max(0d, (nowUtc - DateTime.SpecifyKind(row.LastGameUtc.Value, DateTimeKind.Utc)).TotalDays);

        var breakdown = DedicationScore.Compute(new DedicationInputs(
            PlayRate: row.PlayRate,
            CareerGames: row.CareerGames,
            PatchSpan: row.PatchSpan,
            DaysSinceLastGame: daysSinceLastGame ?? double.PositiveInfinity));

        return new DedicationReadModel
        {
            Score = breakdown.Score,
            ChampionId = row.ChampionId,
            Commitment = breakdown.Commitment,
            Span = breakdown.Span,
            Volume = breakdown.Volume,
            Recency = breakdown.Recency,
            PlayRate = row.PlayRate,
            CareerGames = row.CareerGames,
            PatchSpan = row.PatchSpan,
            DaysSinceLastGame = daysSinceLastGame is null ? null : (int)Math.Floor(daysSinceLastGame.Value),
        };
    }

    /// <summary>One scored account, ready to be ranked by <see cref="DedicationReadModel.Score"/>.</summary>
    internal sealed record DedicationCandidate(Guid AccountId, DedicationReadModel Dedication);

    /// <summary>
    /// The scored, ranked candidate set plus whether the cap actually cut it
    /// short. <see cref="Truncated"/> is only true when eligible accounts were
    /// left out — a population landing exactly on the cap reports false, so the
    /// caller's "migrate to a materialised column" warning never cries wolf.
    /// </summary>
    /// <param name="Candidates">Scored accounts, best first.</param>
    /// <param name="Truncated">True when the cap dropped at least one eligible account.</param>
    internal sealed record DedicationCandidates(
        IReadOnlyList<DedicationCandidate> Candidates,
        bool Truncated)
    {
        public static DedicationCandidates Empty { get; } = new([], false);
    }

    // Raw SQL projection. Nullable LastGameUtc because the LEFT JOIN LATERAL
    // yields NULL for an account whose aggregates haven't been built yet.
    private sealed record DedicationRow(
        Guid AccountId,
        int ChampionId,
        double PlayRate,
        int CareerGames,
        int PatchSpan,
        DateTime? LastGameUtc);
}
