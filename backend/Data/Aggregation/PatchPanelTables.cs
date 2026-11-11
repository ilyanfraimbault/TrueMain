using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Aggregation;

/// <summary>A group of per-patch panel tables that are written and deleted together.</summary>
public enum PatchPanelFamily
{
    /// <summary>Champion vs lane opponent counters and lane verdicts.</summary>
    Matchups,

    /// <summary>Same-team pairs and the baselines they are read against (#922).</summary>
    Synergies,

    /// <summary>Champion vs enemy pairs and their baselines (#1713).</summary>
    Opponents,

    /// <summary>The game-length buckets of the scaling chart (#1368).</summary>
    Scaling,

    /// <summary>Champion profiles, item context counters, totals, verdicts, next-item terms and component orders (#1449, #1450).</summary>
    Context,

    /// <summary>The desktop app's pace (#1912) and form (#2076) benchmarks.</summary>
    Benchmarks
}

/// <summary>One per-patch panel table.</summary>
/// <param name="Family">The family it is deleted and counted with.</param>
/// <param name="Entity">Its entity type.</param>
/// <param name="DatesAPatch">
/// Whether a patch that has rows here exists for retention. The derived tables of the context
/// family (totals, verdicts, next-item terms) only ever hold patches their counters hold too.
/// </param>
public sealed record PatchPanelTable(PatchPanelFamily Family, Type Entity, bool DatesAPatch)
{
    internal Func<TrueMainDbContext, IQueryable<string>> Patches { get; init; } = null!;

    internal Func<TrueMainDbContext, string, CancellationToken, Task<int>> DeletePatch { get; init; } = null!;

    /// <summary>The distinct patches the table holds.</summary>
    public IQueryable<string> PatchesIn(TrueMainDbContext db) => Patches(db);

    /// <summary>Deletes every row of <paramref name="patch"/>; returns how many.</summary>
    public Task<int> DeletePatchAsync(TrueMainDbContext db, string patch, CancellationToken ct) => DeletePatch(db, patch, ct);
}

/// <summary>
/// Every per-patch table behind the champion pages' panels and the desktop benchmarks: what
/// aggregate retention observes, deletes and counts. Adding a panel table means adding it here
/// (<c>PatchPanelTablesTests</c> fails on a <c>Patch</c>-keyed aggregate table that is in
/// neither this list nor its list of exceptions).
/// </summary>
/// <remarks>
/// Not here, on purpose: the aggregate scopes and their patterns (deleted by scope, cascading),
/// and the light per-patch level — <c>champion_patch_stats</c> and the ban pair — which is what
/// is left of a patch once its panels go (#1467, #1956).
/// </remarks>
public static class PatchPanelTables
{
    /// <summary>The tables, in deletion order; a family's tables go in one transaction.</summary>
    public static IReadOnlyList<PatchPanelTable> All { get; } = Array.AsReadOnly(
    [
        Of<ChampionMatchupStat>(PatchPanelFamily.Matchups),
        Of<ChampionSynergyStat>(PatchPanelFamily.Synergies),
        Of<ChampionSynergyBaselineStat>(PatchPanelFamily.Synergies),
        Of<ChampionOpponentStat>(PatchPanelFamily.Opponents),
        Of<ChampionOpponentBaselineStat>(PatchPanelFamily.Opponents),
        Of<ChampionScalingStat>(PatchPanelFamily.Scaling),
        Of<ChampionProfileStat>(PatchPanelFamily.Context),
        Of<ChampionDamageProfileStat>(PatchPanelFamily.Context),
        Of<ChampionItemContextStat>(PatchPanelFamily.Context),
        Of<ChampionItemContextTotal>(PatchPanelFamily.Context, datesAPatch: false),
        Of<ChampionItemContextVerdict>(PatchPanelFamily.Context, datesAPatch: false),
        Of<ChampionNextItemTerm>(PatchPanelFamily.Context, datesAPatch: false),
        Of<ChampionItemComponentOrder>(PatchPanelFamily.Context),
        Of<PaceBenchmarkStat>(PatchPanelFamily.Benchmarks),
        Of<FormBenchmarkStat>(PatchPanelFamily.Benchmarks)
    ]);

    private static PatchPanelTable Of<TEntity>(PatchPanelFamily family, bool datesAPatch = true)
        where TEntity : class
        => new(family, typeof(TEntity), datesAPatch)
        {
            Patches = db => db.Set<TEntity>().AsNoTracking().Select(row => EF.Property<string>(row, PatchColumn)).Distinct(),
            DeletePatch = (db, patch, ct) => db.Set<TEntity>()
                .Where(row => EF.Property<string>(row, PatchColumn) == patch)
                .ExecuteDeleteAsync(ct)
        };

    /// <summary>The property every panel table is keyed by.</summary>
    public const string PatchColumn = "Patch";
}
