using Core.Lol.Patches;
using Data;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes.Components.Retention;

/// <summary>
/// The aggregate half of <c>MatchDataRetentionProcess</c>: dropping the champion
/// aggregates of patches that have fallen out of the retained window. Extracted from the
/// process (#1450) because the situational-context tables made it the sixth family it has
/// to know about, and the process file was already carrying five unrelated retention jobs.
/// </summary>
public static class AggregateRetention
{
    /// <summary>
    /// Deletes champion aggregates for patches older than the
    /// <c>MatchDataRetention:AggregateRetainedPatchCount</c> most recent ones. Disabled by default (0): aggregates are the site's frozen
    /// patch history (#466) and can never be recomputed once their raw matches
    /// are retired, so only small environments (preprod) opt in.
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
        observedPatches.UnionWith(await db.ChampionMatchupStats
            .AsNoTracking().Select(stat => stat.Patch).Distinct().ToListAsync(ct));
        observedPatches.UnionWith(await db.ChampionPowerspikeCurveStats
            .AsNoTracking().Select(stat => stat.Patch).Distinct().ToListAsync(ct));
        observedPatches.UnionWith(await db.ChampionPowerspikeEventStats
            .AsNoTracking().Select(stat => stat.Patch).Distinct().ToListAsync(ct));
        observedPatches.UnionWith(await db.ChampionSynergyStats
            .AsNoTracking().Select(stat => stat.Patch).Distinct().ToListAsync(ct));
        observedPatches.UnionWith(await db.ChampionSynergyBaselineStats
            .AsNoTracking().Select(stat => stat.Patch).Distinct().ToListAsync(ct));
        observedPatches.UnionWith(await db.ChampionBanStats
            .AsNoTracking().Select(stat => stat.Patch).Distinct().ToListAsync(ct));
        observedPatches.UnionWith(await db.BanScopeTotals
            .AsNoTracking().Select(total => total.Patch).Distinct().ToListAsync(ct));
        observedPatches.UnionWith(await db.ChampionProfileStats
            .AsNoTracking().Select(stat => stat.Patch).Distinct().ToListAsync(ct));
        observedPatches.UnionWith(await db.ChampionItemContextStats
            .AsNoTracking().Select(stat => stat.Patch).Distinct().ToListAsync(ct));

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
        // a patch's five tables still go together (no half-deleted patch left
        // behind by an interruption). Global champion_dim_* rows are left
        // alone: they are deduplicated across patches and other scopes may
        // still reference them.
        foreach (var stalePatch in stalePatches)
        {
            ct.ThrowIfCancellationRequested();

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            result = new AggregateDeletionResult(
                result.DeletedScopes + await db.ChampionAggregateScopes
                    .Where(scope => scope.GameVersion == stalePatch).ExecuteDeleteAsync(ct),
                result.DeletedMatchupStats + await db.ChampionMatchupStats
                    .Where(stat => stat.Patch == stalePatch).ExecuteDeleteAsync(ct),
                result.DeletedPowerspikeCurveStats + await db.ChampionPowerspikeCurveStats
                    .Where(stat => stat.Patch == stalePatch).ExecuteDeleteAsync(ct),
                result.DeletedPowerspikeEventStats + await db.ChampionPowerspikeEventStats
                    .Where(stat => stat.Patch == stalePatch).ExecuteDeleteAsync(ct),
                // The synergy pair rows and the baselines they are divided by go in
                // the same transaction as each other: a patch left with baselines but
                // no pairs (or the reverse) would still be read, and would answer with
                // an expected win rate computed against a cohort that no longer exists.
                result.DeletedSynergyStats + await db.ChampionSynergyStats
                    .Where(stat => stat.Patch == stalePatch).ExecuteDeleteAsync(ct)
                    + await db.ChampionSynergyBaselineStats
                        .Where(stat => stat.Patch == stalePatch).ExecuteDeleteAsync(ct),
                // Same reasoning as the synergy pair: the ban counts and the match
                // totals they are divided by must leave together, or the survivor
                // would be read as a rate over a denominator that is gone.
                result.DeletedBanStats + await db.ChampionBanStats
                    .Where(stat => stat.Patch == stalePatch).ExecuteDeleteAsync(ct)
                    + await db.BanScopeTotals
                        .Where(total => total.Patch == stalePatch).ExecuteDeleteAsync(ct),
                // The measured profiles (#1449) and the item context they qualify (#1450).
                // Same rule as the two pairs above and one more reason for it here: a
                // verdict left without the counters it was derived from could never be
                // recomputed, and the counters without their totals are not rates.
                result.DeletedContextStats + await db.ChampionProfileStats
                    .Where(stat => stat.Patch == stalePatch).ExecuteDeleteAsync(ct)
                    + await db.ChampionItemContextStats
                        .Where(stat => stat.Patch == stalePatch).ExecuteDeleteAsync(ct)
                    + await db.ChampionItemContextTotals
                        .Where(total => total.Patch == stalePatch).ExecuteDeleteAsync(ct)
                    + await db.ChampionItemContextVerdicts
                        .Where(verdict => verdict.Patch == stalePatch).ExecuteDeleteAsync(ct));
            await transaction.CommitAsync(ct);
        }

        if (result.TotalDeleted > 0)
        {
            logger.LogInformation(
                "Aggregate retention removed {DeletedScopes} scopes, {DeletedMatchups} matchup, "
                + "{DeletedPowerspikes} powerspike, {DeletedSynergies} synergy and {DeletedBans} ban "
                + "rows for stale patches {StalePatches} (keeping {RetainedPatches}).",
                result.DeletedScopes,
                result.DeletedMatchupStats,
                result.DeletedPowerspikeCurveStats + result.DeletedPowerspikeEventStats,
                result.DeletedSynergyStats,
                result.DeletedBanStats,
                string.Join("|", stalePatches),
                string.Join("|", retainedVersions.OrderDescending().Select(version => version.ToString())));
        }

        return result;
    }
    public sealed record AggregateDeletionResult(
        int DeletedScopes,
        int DeletedMatchupStats,
        int DeletedPowerspikeCurveStats,
        int DeletedPowerspikeEventStats,
        int DeletedSynergyStats,
        int DeletedBanStats,
        // The situational-context family (#1449, #1450): champion profiles, the item
        // context counters and the verdicts derived from them, summed — they are deleted
        // together in one transaction, so one counter describes all four tables.
        int DeletedContextStats)
    {
        public static AggregateDeletionResult Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);

        public int TotalDeleted
            => DeletedScopes
                + DeletedMatchupStats
                + DeletedPowerspikeCurveStats
                + DeletedPowerspikeEventStats
                + DeletedSynergyStats
                + DeletedBanStats
                + DeletedContextStats;
    }
}
