using System.Data.Common;
using System.Text.Json;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Health;

public interface IOverviewQueryService
{
    Task<OverviewReadModel> GetAsync(CancellationToken ct);
}

/// <summary>
/// The admin Overview's counters (<c>GET /ops/stats/overview</c>). No statement on this path
/// scans <c>match_participants</c> or counts <c>matches</c> row by row (#2301): the exact
/// counts and the <c>DISTINCT "ChampionId"</c> over the participant table it used to run
/// pushed the endpoint past the API's statement timeout, leaving the landing page empty.
///
/// <list type="bullet">
/// <item>Corpus totals — the planner's live-tuple estimates (<see cref="RawCorpusEstimates"/>),
/// the figure <c>/health</c> and <c>/database</c> already print.</item>
/// <item>Matches in the last 7 / 30 days — the planner's row estimate for the window. No index
/// leads on <c>GameStartTimeUtc</c>, and retention keeps about a month, so the 30-day window
/// is nearly the whole table: an exact count stays a full pass whatever the index.</item>
/// <item>Champions with games — <c>champion_game_counts</c>, the ingestor's periodic recount
/// of the participant table (#2132).</item>
/// <item>Everything else is exact and small enough to count.</item>
/// </list>
///
/// The reads are independent, so each runs on its own short-lived context, in parallel: the
/// pass costs its slowest read rather than the sum of nine round trips.
/// </summary>
public sealed class OverviewQueryService(
    IDbContextFactory<TrueMainDbContext> dbFactory,
    ILogger<OverviewQueryService> logger) : IOverviewQueryService
{
    internal const string EstimatesUnavailable = "The planner estimates could not be read.";

    public async Task<OverviewReadModel> GetAsync(CancellationToken ct)
    {
        var estimatesTask = SafeEstimatesAsync(ct);
        var trackedAccountsTask = ReadAsync(
            db => db.RiotAccounts.AsNoTracking().CountAsync(ct), ct);
        var candidateCountsTask = ReadAsync(
            db => db.MainCandidates
                .AsNoTracking()
                .GroupBy(candidate => candidate.Status)
                .Select(group => new { Status = group.Key, Count = group.Count() })
                .ToListAsync(ct),
            ct);
        var totalMainsTask = ReadAsync(
            db => db.MainChampionStats
                .AsNoTracking()
                // Active mains only (#900): the pool the public surfaces actually serve.
                .CountAsync(stat => stat.IsMain && stat.IsActive, ct),
            ct);
        var totalOtpsTask = ReadAsync(
            db => db.MainChampionStats.AsNoTracking().CountAsync(stat => stat.IsOtp, ct), ct);
        var championsWithMainsTask = ReadAsync(
            db => db.MainChampionStats
                .AsNoTracking()
                .Where(stat => stat.IsMain && stat.IsActive)
                .Select(stat => stat.ChampionId)
                .Distinct()
                .CountAsync(ct),
            ct);
        var championsWithGamesTask = ReadAsync(
            db => db.ChampionGameCounts
                .AsNoTracking()
                .Where(row => row.Games > 0)
                .Select(row => row.ChampionId)
                .Distinct()
                .CountAsync(ct),
            ct);

        await Task.WhenAll(
            estimatesTask,
            trackedAccountsTask,
            candidateCountsTask,
            totalMainsTask,
            totalOtpsTask,
            championsWithMainsTask,
            championsWithGamesTask);

        var estimates = await estimatesTask;
        var candidateCounts = await candidateCountsTask;

        // Overlay onto the full set of defined statuses so the map always carries every
        // status (zero-filled), giving the frontend a stable shape whichever states exist.
        var candidatesByStatus = Enum.GetValues<MainCandidateStatus>()
            .ToDictionary(
                status => status.ToString(),
                status => candidateCounts.FirstOrDefault(row => row.Status == status)?.Count ?? 0);

        return new OverviewReadModel
        {
            TrackedAccounts = await trackedAccountsTask,
            TotalMatches = estimates.Matches,
            TotalParticipants = estimates.Participants,
            CandidatesByStatus = candidatesByStatus,
            TotalMains = await totalMainsTask,
            TotalOtps = await totalOtpsTask,
            DistinctChampionsWithGames = await championsWithGamesTask,
            DistinctChampionsWithMains = await championsWithMainsTask,
            MatchesLast7Days = estimates.Last7Days,
            MatchesLast30Days = estimates.Last30Days,
            EstimatesUnknownReason = estimates.UnknownReason,
            ComputedAtUtc = DateTime.UtcNow
        };
    }

    private async Task<T> ReadAsync<T>(Func<TrueMainDbContext, Task<T>> read, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await read(db);
    }

    /// <summary>
    /// Degraded like the cockpit's raw-corpus block: a failed estimate costs the match
    /// figures their numbers (null, with the reason), never the whole Overview.
    /// </summary>
    private async Task<OverviewEstimates> SafeEstimatesAsync(CancellationToken ct)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var corpus = await RawCorpusEstimates.ReadAsync(db, ct);
            if (corpus.Matches is null)
            {
                return new OverviewEstimates(null, corpus.Participants, null, null, corpus.UnknownReason);
            }

            await db.Database.OpenConnectionAsync(ct);
            var connection = db.Database.GetDbConnection();
            var last7Days = await PlanRowsAsync(connection, Last7DaysPlan, ct);
            var last30Days = await PlanRowsAsync(connection, Last30DaysPlan, ct);

            // The planner never estimates fewer than one row, and its histogram can trail the
            // live tuple count: a window cannot hold more matches than the table.
            return new OverviewEstimates(
                corpus.Matches,
                corpus.Participants,
                Math.Min(last7Days, corpus.Matches.Value),
                Math.Min(last30Days, corpus.Matches.Value),
                corpus.UnknownReason);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Overview planner estimates failed to read");
            return new OverviewEstimates(null, null, null, null, EstimatesUnavailable);
        }
        finally
        {
            ct.ThrowIfCancellationRequested();
        }
    }

    // The planner's row estimates for the matches started in each window: EXPLAIN plans the
    // query without running it, so these read the column's statistics rather than the rows.
    private const string Last7DaysPlan =
        """EXPLAIN (FORMAT JSON) SELECT 1 FROM matches WHERE "GameStartTimeUtc" >= now() - interval '7 days'""";

    private const string Last30DaysPlan =
        """EXPLAIN (FORMAT JSON) SELECT 1 FROM matches WHERE "GameStartTimeUtc" >= now() - interval '30 days'""";

    private static async Task<long> PlanRowsAsync(DbConnection connection, string explainSql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = explainSql;
        var plan = await command.ExecuteScalarAsync(ct) as string
            ?? throw new InvalidOperationException("EXPLAIN returned no plan.");
        return PlanRows(plan);
    }

    internal static long PlanRows(string explainJson)
    {
        using var document = JsonDocument.Parse(explainJson);
        var rows = document.RootElement[0].GetProperty("Plan").GetProperty("Plan Rows").GetDouble();
        return (long)Math.Round(rows);
    }

    private sealed record OverviewEstimates(
        long? Matches,
        long? Participants,
        long? Last7Days,
        long? Last30Days,
        string? UnknownReason);
}
