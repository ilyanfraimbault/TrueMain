using Core.Truemains;
using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains.Leaderboard;

/// <summary>
/// Loads the inputs of the dedication score — shown as the <em>Truemain score</em>
/// (#1701) — and turns them into the read model
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
/// Every input lives on the signature champion's <c>main_champion_stats</c> row:
/// the play rate from main analysis' rolling window over recent ranked games,
/// and the mastery points, rank and last-play time <c>MainActivityProcess</c>
/// reads from Riot (#1701). The score used to measure a career from
/// <c>champion_aggregate_scopes</c>, which only counted what TrueMain had
/// tracked; mastery measures the player instead. Unread mastery (a main
/// MainActivity has not reached yet) scores 0 on those parts and the surface
/// says it is pending.
/// </para>
/// <para>
/// Everything is computed at read time. Nothing here is materialised: the score
/// changes whenever main analysis or the mastery check rewrites the row, and
/// the inputs are a handful of columns, so a per-request read stays cheaper than keeping
/// a denormalised column honest.
/// </para>
/// </remarks>
internal static class MainDedication
{
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
    /// <param name="nowUtc">Clock reference for the days-since-last-played figure.</param>
    /// <param name="commitmentFloor">
    /// Live <c>MainAnalysis:PlayRateFloor</c> — the play rate below which no
    /// champion is a main, and therefore the point commitment reads 0 (#869).
    /// </param>
    /// <param name="ct">Request cancellation token.</param>
    public static async Task<Dictionary<Guid, DedicationReadModel>> FetchAsync(
        TrueMainDbContext ctx,
        Guid[] accountIds,
        int? championId,
        DateTime nowUtc,
        double commitmentFloor,
        CancellationToken ct)
    {
        if (accountIds.Length == 0)
        {
            return new Dictionary<Guid, DedicationReadModel>();
        }

        // DISTINCT ON collapses each account to one signature champion inside
        // the database. Every input lives on that main_champion_stats row: play
        // rate from main analysis, mastery from MainActivity (#1701).
        FormattableString sql = $"""
            SELECT DISTINCT ON (a."Id")
                a."Id" AS "AccountId",
                m."ChampionId" AS "ChampionId",
                m."PlayRate" AS "PlayRate",
                m."ChampionMatches" AS "ChampionMatches",
                m."TotalMatches" AS "TotalMatches",
                m."IsOtp" AS "IsOtp",
                m."MasteryPoints" AS "MasteryPoints",
                m."MasteryRank" AS "MasteryRank",
                m."MasteryLastPlayUtc" AS "MasteryLastPlayUtc"
            FROM riot_accounts a
            JOIN main_champion_stats m
              ON m."PlatformId" = a."PlatformId" AND m."Puuid" = a."Puuid"
            WHERE a."Id" = ANY ({accountIds})
              AND m."IsMain" = true
              AND m."IsActive" = true
              AND ({championId}::int IS NULL OR m."ChampionId" = {championId})
            ORDER BY a."Id", m."PlayRate" DESC, m."ChampionMatches" DESC
            """;

        var rows = await ctx.Database.SqlQuery<DedicationRow>(sql).ToListAsync(ct);
        return rows.ToDictionary(row => row.AccountId, row => Project(row, nowUtc, commitmentFloor));
    }

    private static DedicationReadModel Project(DedicationRow row, DateTime nowUtc, double commitmentFloor)
    {
        var breakdown = DedicationScore.Compute(
            new DedicationInputs(row.PlayRate, row.MasteryPoints, row.MasteryRank),
            commitmentFloor);

        int? daysSinceLastPlayed = row.MasteryLastPlayUtc is null
            ? null
            : (int)Math.Floor(Math.Max(0d, (nowUtc - DateTime.SpecifyKind(row.MasteryLastPlayUtc.Value, DateTimeKind.Utc)).TotalDays));

        return new DedicationReadModel
        {
            Score = breakdown.Score,
            ChampionId = row.ChampionId,
            IsOtp = row.IsOtp,
            PlayRate = row.PlayRate,
            ChampionGames = row.ChampionMatches,
            RecentGames = row.TotalMatches,
            MasteryPoints = row.MasteryPoints,
            MasteryRank = row.MasteryRank,
            DaysSinceLastPlayed = daysSinceLastPlayed,
            Parts =
            [
                Part(DedicationPartKeys.PlayRate, DedicationScore.CommitmentWeight, breakdown.Commitment),
                Part(DedicationPartKeys.Mastery, DedicationScore.MasteryWeight, breakdown.Mastery),
                Part(DedicationPartKeys.MasteryRank, DedicationScore.MasteryRankWeight, breakdown.MasteryRank),
            ],
        };
    }

    private static DedicationPartReadModel Part(string key, double weight, double component)
        => new() { Key = key, Points = 100d * weight * component, MaxPoints = 100d * weight };

    // Raw SQL projection. Mastery columns are null until MainActivity has read
    // the account's mastery (#1701).
    private sealed record DedicationRow(
        Guid AccountId,
        int ChampionId,
        double PlayRate,
        int ChampionMatches,
        int TotalMatches,
        bool IsOtp,
        long? MasteryPoints,
        int? MasteryRank,
        DateTime? MasteryLastPlayUtc);
}
