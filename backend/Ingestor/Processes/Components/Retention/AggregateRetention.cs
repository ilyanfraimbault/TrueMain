using Core.Lol.Patches;
using Data;
using Data.Aggregation;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes.Components.Retention;

/// <summary>
/// The aggregate half of <c>MatchDataRetentionProcess</c>: dropping the champion
/// aggregates of patches that have fallen out of the retained window. Extracted from the
/// process (#1450). Which tables a patch's aggregates are is not known here but in
/// <see cref="PatchPanelTables"/>.
/// </summary>
public static class AggregateRetention
{
    /// <summary>
    /// Deletes champion aggregates for patches older than the
    /// <c>MatchDataRetention:AggregateRetainedPatchCount</c> most recent ones. Disabled by default (0): aggregates are the site's frozen
    /// patch history (#466) and can never be recomputed once their raw matches
    /// are retired, so only small environments (preprod) opt in.
    ///
    /// <para>
    /// The light per-patch level is never touched (#1467, #1956): <c>champion_patch_stats</c>
    /// and the ban pair (<c>champion_ban_stats</c> / <c>ban_scope_totals</c>) are what is
    /// left of a patch once its full aggregates go, so neither is deleted nor counted among
    /// the patches observed here.
    /// </para>
    /// </summary>
    public static async Task<AggregateDeletionResult> DeleteExpiredAsync(
        IDbContextFactory<TrueMainDbContext> dbContextFactory,
        int retainedPatchCount,
        ILogger logger,
        CancellationToken ct)
    {
        if (retainedPatchCount <= 0)
        {
            return AggregateDeletionResult.Empty;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var observedPatches = new HashSet<string>(StringComparer.Ordinal);
        observedPatches.UnionWith(await db.ChampionAggregateScopes
            .AsNoTracking().Select(scope => scope.GameVersion).Distinct().ToListAsync(ct));
        foreach (var table in PatchPanelTables.All.Where(table => table.DatesAPatch))
        {
            observedPatches.UnionWith(await table.PatchesIn(db).ToListAsync(ct));
        }

        // Rank the observed patch strings by parsed version and keep the N most
        // recent. Unparseable strings are never deleted — better to leave an odd
        // row behind than to wipe data on a format surprise.
        var parsedPatches = observedPatches
            .Select(raw => PatchVersion.TryParse(raw, out var version)
                ? (Raw: raw, Version: version)
                : default((string Raw, PatchVersion Version)?))
            .Where(entry => entry is not null)
            .Select(entry => entry!.Value)
            .ToList();

        var retainedVersions = parsedPatches
            .Select(entry => new PatchVersion(entry.Version.Major, entry.Version.Minor))
            .Distinct()
            .OrderDescending()
            .Take(retainedPatchCount)
            .ToHashSet();

        var stalePatches = parsedPatches
            .Where(entry => !retainedVersions.Contains(new PatchVersion(entry.Version.Major, entry.Version.Minor)))
            .Select(entry => entry.Raw)
            .Order(StringComparer.Ordinal)
            .ToList();

        if (stalePatches.Count == 0)
        {
            return AggregateDeletionResult.Empty;
        }

        var result = AggregateDeletionResult.Empty;

        // One patch per transaction keeps each delete's lock footprint and WAL
        // bounded — a scope delete cascades to its pattern rows, and years of
        // frozen patches could otherwise pile into one huge transaction — while
        // a patch's tables still go together (no half-deleted patch left
        // behind by an interruption). Global champion_dim_* rows are left
        // alone: they are deduplicated across patches and other scopes may
        // still reference them.
        foreach (var stalePatch in stalePatches)
        {
            ct.ThrowIfCancellationRequested();

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var deletedScopes = await db.ChampionAggregateScopes
                .Where(scope => scope.GameVersion == stalePatch).ExecuteDeleteAsync(ct);
            result = result
                .Plus(await DeletePanelStatsAsync(db, stalePatch, ct))
                .Plus(AggregateDeletionResult.Empty with { DeletedScopes = deletedScopes });
            await transaction.CommitAsync(ct);
        }

        if (result.TotalDeleted > 0)
        {
            logger.LogInformation(
                "Aggregate retention removed {DeletedScopes} scopes, {DeletedMatchups} matchup "
                + "and {DeletedSynergies} synergy rows for stale patches {StalePatches} (keeping {RetainedPatches}).",
                result.DeletedScopes,
                result.Deleted(PatchPanelFamily.Matchups),
                result.Deleted(PatchPanelFamily.Synergies),
                string.Join("|", stalePatches),
                string.Join("|", retainedVersions.OrderDescending().Select(version => version.ToString())));
        }

        return result;
    }

    /// <summary>
    /// Every <see cref="PatchPanelTables"/> table of one patch: everything per-patch except the
    /// scopes and their patterns. Shared by the full purge (which also drops the scopes) and the
    /// build purge (#1958, which keeps them), so the two can never disagree on what "a patch's
    /// panels" are. Runs inside the caller's transaction, so a family's tables — pairs and the
    /// baselines they are divided by, counters and the verdicts derived from them — go together.
    /// </summary>
    internal static async Task<AggregateDeletionResult> DeletePanelStatsAsync(
        TrueMainDbContext db,
        string patch,
        CancellationToken ct)
    {
        var byFamily = new Dictionary<PatchPanelFamily, int>();
        foreach (var table in PatchPanelTables.All)
        {
            byFamily[table.Family] = byFamily.GetValueOrDefault(table.Family) + await table.DeletePatchAsync(db, patch, ct);
        }

        return new AggregateDeletionResult(0, byFamily);
    }

    /// <summary>Rows deleted: the scopes, then the panel tables per family.</summary>
    public sealed record AggregateDeletionResult(int DeletedScopes, IReadOnlyDictionary<PatchPanelFamily, int> Panels)
    {
        public static AggregateDeletionResult Empty { get; } = new(0, new Dictionary<PatchPanelFamily, int>());

        /// <summary>The rows of one family.</summary>
        public int Deleted(PatchPanelFamily family) => Panels.GetValueOrDefault(family);

        public AggregateDeletionResult Plus(AggregateDeletionResult other)
            => new(
                DeletedScopes + other.DeletedScopes,
                Enum.GetValues<PatchPanelFamily>().ToDictionary(family => family, family => Deleted(family) + other.Deleted(family)));

        public int TotalDeleted => DeletedScopes + Panels.Values.Sum();
    }
}
