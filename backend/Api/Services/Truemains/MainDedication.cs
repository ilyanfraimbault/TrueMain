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
/// pick a player's top champion). When the caller passes a champion filter, the
/// score is about that champion instead: the leaderboard filtered to Yasuo must
/// rank Yasuo dedication, not each player's unrelated top main.
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
    /// Dedication for a known set of accounts (a leaderboard page slice, or a
    /// single profile). Accounts with no classified main are absent from the
    /// result — the caller renders nothing rather than a zero.
    /// </summary>
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
    /// The candidate scan is capped at <paramref name="limit"/> rows taken by
    /// descending play rate. Play rate carries the heaviest weight
    /// (<see cref="DedicationScore.CommitmentWeight"/>), so the rows the cap
    /// drops are the least committed of the population — the safety valve
    /// protects the API from an unbounded scan if the tracked population grows
    /// by orders of magnitude, at the cost of the deep tail of the ranking.
    /// </remarks>
    public static async Task<List<DedicationCandidate>> FetchCandidatesAsync(
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
            return [];
        }

        // The WHERE clause must stay in lock-step with
        // TruemainsLeaderboardQueryService.CountAsync: the same population, just
        // resolved to one row per account (DISTINCT ON) instead of tested with
        // EXISTS, so the total and the ranked slice agree on who is eligible.
        FormattableString sql = $"""
            WITH mains AS (
                SELECT DISTINCT ON (a."Id")
                    a."Id" AS "AccountId",
                    m."ChampionId" AS "ChampionId",
                    m."PlayRate" AS "PlayRate"
                FROM riot_accounts a
                JOIN main_champion_stats m
                  ON m."PlatformId" = a."PlatformId" AND m."Puuid" = a."Puuid"
                WHERE a."PlatformId" = ANY ({platforms})
                  AND a."Score" IS NOT NULL
                  AND m."IsMain" = true
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
            ),
            capped AS (
                SELECT * FROM mains ORDER BY mains."PlayRate" DESC, mains."AccountId" LIMIT {limit}
            )
            SELECT
                capped."AccountId" AS "AccountId",
                capped."ChampionId" AS "ChampionId",
                capped."PlayRate" AS "PlayRate",
                COALESCE(career."CareerGames", 0) AS "CareerGames",
                COALESCE(career."PatchSpan", 0) AS "PatchSpan",
                career."LastGameUtc" AS "LastGameUtc"
            FROM capped
            LEFT JOIN LATERAL (
                SELECT
                    SUM(s."Games")::int AS "CareerGames",
                    COUNT(DISTINCT s."GameVersion")::int AS "PatchSpan",
                    MAX(s."LastGameStartTimeUtc") AS "LastGameUtc"
                FROM champion_aggregate_scopes s
                WHERE s."RiotAccountId" = capped."AccountId"
                  AND s."ChampionId" = capped."ChampionId"
                  AND s."QueueId" = {RankedQueueId}
            ) career ON TRUE
            """;

        var rows = await ctx.Database.SqlQuery<DedicationRow>(sql).ToListAsync(ct);

        return rows
            .Select(row => new DedicationCandidate(row.AccountId, Project(row, nowUtc)))
            // Descending score, then a deterministic tiebreak on the account id
            // so two genuinely tied players keep a stable order across requests
            // (otherwise page 2 could repeat or skip a row).
            .OrderByDescending(candidate => candidate.Dedication.Score)
            .ThenBy(candidate => candidate.AccountId)
            .ToList();
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
