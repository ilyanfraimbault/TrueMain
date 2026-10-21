using Data;
using Data.Ops.Mongo;
using Ingestor.Processes.Summaries;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes;

/// <summary>
/// Recomputes <c>champion_game_counts</c> (#2132) — stored participants per
/// <c>(platform, patch, queue, position, champion)</c> — from the raw rows, so the
/// operator champion stats read a few thousand rollup rows instead of grouping the whole
/// <c>match_participants</c> table on request, which hit the API's 60 s
/// <c>statement_timeout</c> whenever its cache was cold.
///
/// <para>
/// <b>A recompute, not an incremental fold</b> — the one deliberate exception to "never a
/// full recompute" (#811). The figure is a count of what the raw table holds <em>now</em>:
/// retention deleting a patch must take its games away, and the lane fix moving a
/// participant to another position must move its game. A fold-once counter flagged on
/// <c>matches</c> would keep both, and would need a decrement path in retention and in
/// every participant rewriter to stay right. What made the self-join recomputes of #811
/// unaffordable does not apply either: this is one grouped pass over the retained window
/// with no join beyond the match's own row.
/// </para>
///
/// <para>
/// <b>One statement for the whole rollup</b> (#2217). Slicing it by
/// <c>(queue, patch, platform)</c> bounded nothing: those columns live on <c>matches</c>
/// only, so every large slice hash-joined against a full scan of the participant index,
/// and a run read <c>match_participants</c> once per large slice — each pass long enough
/// on its own to hit the 300 s command timeout under load. The whole table is now
/// replaced in one transaction, which a reader sees either before or after, under
/// <see cref="CommandTimeout"/> instead of the connection's default; slices no stored
/// match belongs to any more vanish with the delete.
/// </para>
///
/// <para>
/// Throttled to <see cref="MinRunInterval"/>, measured from the last run that did its
/// work: the aggregate lane iterates back to back, and the operator page this feeds caches
/// its answer for ten minutes anyway. It runs after <see cref="MatchDataRetentionProcess"/>
/// so the count it stores is the one retention leaves, not a peak about to be deleted.
/// </para>
/// </summary>
public sealed class ChampionGameCountRefreshProcess(
    ILogger<ChampionGameCountRefreshProcess> logger,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    IProcessRunStore processRunStore,
    TimeProvider timeProvider) : IIngestorProcess
{
    /// <summary>
    /// How stale the rollup may get. Half an hour keeps the cost at two passes over the
    /// retained participants per hour at most, against an operator figure nobody reads at
    /// a finer grain than the aggregate lane's own cadence.
    /// </summary>
    internal static readonly TimeSpan MinRunInterval = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The bound on the single pass. Its duration swings with concurrent load — ingestion
    /// writes and autovacuum compete for the same participant pages — so it gets more room
    /// than the connection's 300 s; a pass that still overruns fails the run, and the
    /// previous rollup stays in place until the next one.
    /// </summary>
    internal static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(15);

    public string Name => "ChampionGameCountRefresh";

    public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var lastRunUtc = await processRunStore.GetLastCompletedRunStartAsync(Name, ct);
        if (lastRunUtc is not null && nowUtc - lastRunUtc.Value < MinRunInterval)
        {
            logger.LogInformation(
                "Champion game count refresh skipped: last run {LastRunUtc:o} is within {Interval}.",
                lastRunUtc,
                MinRunInterval);
            return new SkippedSummary("Within MinRunInterval; champion game counts skipped this iteration.", true);
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        db.Database.SetCommandTimeout(CommandTimeout);

        var previousSlices = await LoadSlicesAsync(db, ct);

        int rows;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await db.ChampionGameCounts.ExecuteDeleteAsync(ct);

            // A null Patch (an unparseable GameVersion) is its own slice, stored under the
            // empty string.
            rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO champion_game_counts
                    ("QueueId", "Patch", "PlatformId", "TeamPosition", "ChampionId", "Games", "RefreshedAtUtc")
                SELECT m."QueueId", COALESCE(m."Patch", ''), m."PlatformId", p."TeamPosition", p."ChampionId", COUNT(*), {nowUtc}
                FROM matches m
                INNER JOIN match_participants p ON p."MatchId" = m."Id"
                GROUP BY m."QueueId", COALESCE(m."Patch", ''), m."PlatformId", p."TeamPosition", p."ChampionId"
                """, ct);

            await transaction.CommitAsync(ct);
        }

        var slices = await LoadSlicesAsync(db, ct);
        var removed = previousSlices.Except(slices).Count();
        var games = await db.ChampionGameCounts.AsNoTracking().SumAsync(row => row.Games, ct);

        logger.LogInformation(
            "Champion game count refresh summary: slices={Slices}, removed={Removed}, rows={Rows}, games={Games}.",
            slices.Count,
            removed,
            rows,
            games);

        return new ChampionGameCountRefreshSummary(slices.Count, removed, rows, games);
    }

    private static Task<List<GameCountSlice>> LoadSlicesAsync(TrueMainDbContext db, CancellationToken ct)
        => db.ChampionGameCounts
            .AsNoTracking()
            .Select(row => new GameCountSlice(row.QueueId, row.Patch, row.PlatformId))
            .Distinct()
            .ToListAsync(ct);

    private sealed record GameCountSlice(int QueueId, string Patch, string PlatformId);
}
