using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes.Components.Retention;

/// <summary>
/// Production's half of the two-level aggregate retention (#1467, #1958): a patch that
/// has left every platform's live window loses its build-level champion aggregates —
/// the patterns and the per-patch panel tables — and keeps its account-level scopes and
/// its light record. 95% of the aggregates' weight is the patterns; the scopes are what
/// the truemains leaderboard and the persona "last game" sum over every patch, so keeping
/// them keeps those figures whole for a few percent of the storage.
/// </summary>
public static class BuildAggregateRetention
{
    /// <summary>
    /// Scopes whose patterns are deleted per transaction. A prod patch holds ~130k scopes
    /// and ~700k patterns; one statement for all of them would hold its locks and WAL for
    /// minutes, while a chunk commits in seconds and an interrupted run resumes where it
    /// stopped (the scopes it already emptied simply match no pattern).
    /// </summary>
    private const int ScopeChunkSize = 5000;

    public static async Task<BuildPurgeResult> PurgeOutsideWindowAsync(
        IDbContextFactory<TrueMainDbContext> dbContextFactory,
        int queueId,
        int retainedPatchCount,
        ILogger logger,
        CancellationToken ct)
    {
        if (retainedPatchCount <= 0)
        {
            return BuildPurgeResult.Empty;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        // "Live" means what it means to the pattern aggregation: the most recent patches each
        // platform has played, ranked by their newest game so a straggler cannot revive a
        // retired one (#466, #1549). A patch still live on any platform keeps its builds.
        var window = await RetainedPatchWindow.LoadAsync(db, queueId, retainedPatchCount, ct);
        var livePatches = window.Values.SelectMany(patches => patches).ToHashSet(StringComparer.Ordinal);
        if (livePatches.Count == 0)
        {
            return BuildPurgeResult.Empty;
        }

        var alreadyPurged = await db.ChampionBuildPurgedPatches
            .AsNoTracking().Select(purged => purged.Patch).ToListAsync(ct);
        var scopePatches = await db.ChampionAggregateScopes
            .AsNoTracking().Select(scope => scope.GameVersion).Distinct().ToListAsync(ct);

        var candidates = scopePatches
            .Where(patch => !livePatches.Contains(patch))
            .Except(alreadyPurged, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var result = BuildPurgeResult.Empty;
        foreach (var patch in candidates)
        {
            ct.ThrowIfCancellationRequested();

            // The light record is what is left of a patch once its builds go, so a patch
            // without one keeps everything: purging first and backfilling later would lose
            // the history for good (#1467).
            if (!await db.ChampionPatchStats.AsNoTracking().AnyAsync(stat => stat.Patch == patch, ct))
            {
                logger.LogWarning(
                    "Build retention skipped patch {Patch}: it has no light per-patch record yet.", patch);
                result = result with { SkippedPatches = [.. result.SkippedPatches, patch] };
                continue;
            }

            var deletedPatterns = await DeletePatternsAsync(db, patch, ct);

            // The panel tables and the marker go together: a patch is only marked purged once
            // nothing of its builds is left to render.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var panels = await AggregateRetention.DeletePanelStatsAsync(db, patch, ct);
            db.ChampionBuildPurgedPatches.Add(new ChampionBuildPurgedPatch { Patch = patch, PurgedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            result = result with
            {
                PurgedPatches = [.. result.PurgedPatches, patch],
                DeletedPatterns = result.DeletedPatterns + deletedPatterns,
                DeletedPanelRows = result.DeletedPanelRows + panels.TotalDeleted,
            };
        }

        if (result.PurgedPatches.Count > 0 || result.SkippedPatches.Count > 0)
        {
            logger.LogInformation(
                "Build retention purged patches {PurgedPatches} ({DeletedPatterns} patterns, {DeletedPanelRows} panel rows), "
                + "skipped {SkippedPatches}, keeping live {LivePatches}; their scopes and light records stay.",
                string.Join("|", result.PurgedPatches),
                result.DeletedPatterns,
                result.DeletedPanelRows,
                string.Join("|", result.SkippedPatches),
                string.Join("|", livePatches.Order(StringComparer.Ordinal)));
        }

        return result;
    }

    private static async Task<int> DeletePatternsAsync(TrueMainDbContext db, string patch, CancellationToken ct)
    {
        var scopeIds = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.GameVersion == patch)
            .Select(scope => scope.Id)
            .ToListAsync(ct);

        var deleted = 0;
        foreach (var chunk in scopeIds.Chunk(ScopeChunkSize))
        {
            ct.ThrowIfCancellationRequested();
            deleted += await db.ChampionAggregatePatterns
                .Where(pattern => chunk.Contains(pattern.ScopeId))
                .ExecuteDeleteAsync(ct);
        }

        return deleted;
    }

    public sealed record BuildPurgeResult(
        IReadOnlyList<string> PurgedPatches,
        IReadOnlyList<string> SkippedPatches,
        int DeletedPatterns,
        int DeletedPanelRows)
    {
        public static BuildPurgeResult Empty { get; } = new([], [], 0, 0);
    }
}
