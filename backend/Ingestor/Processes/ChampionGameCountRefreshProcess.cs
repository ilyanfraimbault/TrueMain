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
/// with no join beyond the match's own row, done one <c>(queue, patch, platform)</c> slice
/// per statement — the grain of <c>IX_matches_queue_patch_platform</c> — so no statement
/// spans the corpus and each slice is swapped atomically under the readers.
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

        // An index-only scan of IX_matches_queue_patch_platform. A null Patch (an
        // unparseable GameVersion) is its own slice, stored under the empty string.
        var sourceSlices = await db.Database
            .SqlQuery<GameCountSlice>($"""
                SELECT DISTINCT "QueueId", COALESCE("Patch", '') AS "Patch", "PlatformId"
                FROM matches
                """)
            .ToListAsync(ct);

        var storedSlices = await db.ChampionGameCounts
            .AsNoTracking()
            .Select(row => new GameCountSlice(row.QueueId, row.Patch, row.PlatformId))
            .Distinct()
            .ToListAsync(ct);

        var rows = 0;
        foreach (var slice in sourceSlices)
        {
            rows += await RefreshSliceAsync(db, slice, nowUtc, ct);
        }

        // Slices no stored match belongs to any more: retention dropped their patch, or
        // drained their queue. Left in place they would keep counting deleted games.
        var removed = storedSlices.Except(sourceSlices).ToList();
        foreach (var slice in removed)
        {
            await db.ChampionGameCounts
                .Where(row => row.QueueId == slice.QueueId
                    && row.Patch == slice.Patch
                    && row.PlatformId == slice.PlatformId)
                .ExecuteDeleteAsync(ct);
        }

        var games = await db.ChampionGameCounts.AsNoTracking().SumAsync(row => row.Games, ct);

        logger.LogInformation(
            "Champion game count refresh summary: slices={Slices}, removed={Removed}, rows={Rows}, games={Games}.",
            sourceSlices.Count,
            removed.Count,
            rows,
            games);

        return new ChampionGameCountRefreshSummary(sourceSlices.Count, removed.Count, rows, games);
    }

    /// <summary>
    /// Replaces one slice with its count over the raw rows, in one transaction, so a
    /// reader sees either the previous count of the slice or the new one — never the
    /// slice half-deleted.
    /// </summary>
    private static async Task<int> RefreshSliceAsync(
        TrueMainDbContext db,
        GameCountSlice slice,
        DateTime nowUtc,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await db.ChampionGameCounts
            .Where(row => row.QueueId == slice.QueueId
                && row.Patch == slice.Patch
                && row.PlatformId == slice.PlatformId)
            .ExecuteDeleteAsync(ct);

        // Two spellings rather than one "IS NOT DISTINCT FROM", which no index serves.
        var inserted = slice.Patch.Length == 0
            ? await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO champion_game_counts
                    ("QueueId", "Patch", "PlatformId", "TeamPosition", "ChampionId", "Games", "RefreshedAtUtc")
                SELECT m."QueueId", '', m."PlatformId", p."TeamPosition", p."ChampionId", COUNT(*), {nowUtc}
                FROM matches m
                INNER JOIN match_participants p ON p."MatchId" = m."Id"
                WHERE m."QueueId" = {slice.QueueId}
                  AND m."Patch" IS NULL
                  AND m."PlatformId" = {slice.PlatformId}
                GROUP BY m."QueueId", m."PlatformId", p."TeamPosition", p."ChampionId"
                """, ct)
            : await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO champion_game_counts
                    ("QueueId", "Patch", "PlatformId", "TeamPosition", "ChampionId", "Games", "RefreshedAtUtc")
                SELECT m."QueueId", m."Patch", m."PlatformId", p."TeamPosition", p."ChampionId", COUNT(*), {nowUtc}
                FROM matches m
                INNER JOIN match_participants p ON p."MatchId" = m."Id"
                WHERE m."QueueId" = {slice.QueueId}
                  AND m."Patch" = {slice.Patch}
                  AND m."PlatformId" = {slice.PlatformId}
                GROUP BY m."QueueId", m."Patch", m."PlatformId", p."TeamPosition", p."ChampionId"
                """, ct);

        await transaction.CommitAsync(ct);
        return inserted;
    }

    private sealed record GameCountSlice(int QueueId, string Patch, string PlatformId);
}
