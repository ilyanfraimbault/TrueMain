using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.Services.Champions;

/// <summary>
/// Phase 6.3 — loads <see cref="ChampionAggregatePattern"/> rows for a set
/// of scopes and projects them into the per-dimension shapes the existing
/// aggregators consume. Optional <see cref="ChampionPatternPivot"/> filters
/// the patterns first so the projected dimensions reflect a "given build X,
/// what else?" view.
/// </summary>
internal static class ChampionPatternProjector
{
    public static async Task<ChampionPatternProjection> ProjectAsync(
        TrueMainDbContext db,
        IReadOnlyList<Guid> scopeIds,
        ChampionPatternPivot pivot,
        CancellationToken ct)
    {
        if (scopeIds.Count == 0)
        {
            return ChampionPatternProjection.Empty;
        }

        var patterns = db.ChampionAggregatePatterns.AsNoTracking()
            .Where(pattern => scopeIds.Contains(pattern.ScopeId));

        if (pivot.BuildId.HasValue)
        {
            patterns = patterns.Where(pattern => pattern.BuildId == pivot.BuildId.Value);
        }

        var builds = await ProjectBuildsAsync(db, patterns, ct);
        var runePages = await ProjectRunePagesAsync(db, patterns, ct);
        var skillOrders = await ProjectSkillOrdersAsync(db, patterns, ct);
        var spellPairs = await ProjectSpellPairsAsync(db, patterns, ct);
        var starterItems = await ProjectStarterItemsAsync(db, patterns, ct);

        return new ChampionPatternProjection(builds, runePages, skillOrders, spellPairs, starterItems);
    }

    private static async Task<IReadOnlyList<ChampionAggregateBuild>> ProjectBuildsAsync(
        TrueMainDbContext db,
        IQueryable<ChampionAggregatePattern> patterns,
        CancellationToken ct)
    {
        var aggregates = await patterns
            .GroupBy(pattern => pattern.BuildId)
            .Select(group => new
            {
                DimId = group.Key,
                Games = group.Sum(pattern => pattern.Games),
                Wins = group.Sum(pattern => pattern.Wins)
            })
            .Join(
                db.ChampionDimBuilds.AsNoTracking(),
                aggregate => aggregate.DimId,
                dim => dim.Id,
                (aggregate, dim) => new ChampionAggregateBuild
                {
                    BootsItemId = dim.BootsItemId,
                    BuildItem0 = dim.BuildItem0,
                    BuildItem1 = dim.BuildItem1,
                    BuildItem2 = dim.BuildItem2,
                    BuildItem3 = dim.BuildItem3,
                    BuildItem4 = dim.BuildItem4,
                    BuildItem5 = dim.BuildItem5,
                    BuildItem6 = dim.BuildItem6,
                    Games = aggregate.Games,
                    Wins = aggregate.Wins
                })
            .ToListAsync(ct);
        return aggregates;
    }

    private static async Task<IReadOnlyList<ChampionAggregateRunePage>> ProjectRunePagesAsync(
        TrueMainDbContext db,
        IQueryable<ChampionAggregatePattern> patterns,
        CancellationToken ct)
    {
        // Group by (BuildId, RunePageId) instead of just RunePageId so the
        // result preserves the build correlation. We then hydrate
        // FirstItemId from ChampionDimBuilds.BuildItem0 — this matches the
        // legacy ChampionAggregateRunePage.FirstItemId semantics that
        // ChampionRunePageAggregator.SelectTopForFirstItem (used by the
        // build tree) keys on. The foundation aggregator collapses across
        // FirstItemId so it sees the same totals it would with a flat
        // RunePageId grouping.
        var aggregates = await patterns
            .GroupBy(pattern => new { pattern.BuildId, pattern.RunePageId })
            .Select(group => new
            {
                group.Key.BuildId,
                group.Key.RunePageId,
                Games = group.Sum(pattern => pattern.Games),
                Wins = group.Sum(pattern => pattern.Wins)
            })
            .Join(
                db.ChampionDimRunePages.AsNoTracking(),
                aggregate => aggregate.RunePageId,
                dim => dim.Id,
                (aggregate, dim) => new { Aggregate = aggregate, Rune = dim })
            .Join(
                db.ChampionDimBuilds.AsNoTracking(),
                joined => joined.Aggregate.BuildId,
                build => build.Id,
                (joined, build) => new ChampionAggregateRunePage
                {
                    FirstItemId = build.BuildItem0,
                    PrimaryStyleId = joined.Rune.PrimaryStyleId,
                    PrimaryKeystoneId = joined.Rune.PrimaryKeystoneId,
                    PrimaryPerk1Id = joined.Rune.PrimaryPerk1Id,
                    PrimaryPerk2Id = joined.Rune.PrimaryPerk2Id,
                    PrimaryPerk3Id = joined.Rune.PrimaryPerk3Id,
                    SecondaryStyleId = joined.Rune.SecondaryStyleId,
                    SecondaryPerk1Id = joined.Rune.SecondaryPerk1Id,
                    SecondaryPerk2Id = joined.Rune.SecondaryPerk2Id,
                    StatOffense = joined.Rune.StatOffense,
                    StatFlex = joined.Rune.StatFlex,
                    StatDefense = joined.Rune.StatDefense,
                    Games = joined.Aggregate.Games,
                    Wins = joined.Aggregate.Wins
                })
            .ToListAsync(ct);
        return aggregates;
    }

    private static async Task<IReadOnlyList<ChampionAggregateSkillOrder>> ProjectSkillOrdersAsync(
        TrueMainDbContext db,
        IQueryable<ChampionAggregatePattern> patterns,
        CancellationToken ct)
    {
        var aggregates = await patterns
            .GroupBy(pattern => pattern.SkillOrderId)
            .Select(group => new
            {
                DimId = group.Key,
                Games = group.Sum(pattern => pattern.Games),
                Wins = group.Sum(pattern => pattern.Wins)
            })
            .Join(
                db.ChampionDimSkillOrders.AsNoTracking(),
                aggregate => aggregate.DimId,
                dim => dim.Id,
                (aggregate, dim) => new ChampionAggregateSkillOrder
                {
                    SkillOrderKey = dim.SkillOrderKey,
                    Games = aggregate.Games,
                    Wins = aggregate.Wins
                })
            .ToListAsync(ct);
        return aggregates;
    }

    private static async Task<IReadOnlyList<ChampionAggregateSpellPair>> ProjectSpellPairsAsync(
        TrueMainDbContext db,
        IQueryable<ChampionAggregatePattern> patterns,
        CancellationToken ct)
    {
        var aggregates = await patterns
            .GroupBy(pattern => pattern.SpellPairId)
            .Select(group => new
            {
                DimId = group.Key,
                Games = group.Sum(pattern => pattern.Games),
                Wins = group.Sum(pattern => pattern.Wins)
            })
            .Join(
                db.ChampionDimSpellPairs.AsNoTracking(),
                aggregate => aggregate.DimId,
                dim => dim.Id,
                (aggregate, dim) => new ChampionAggregateSpellPair
                {
                    Spell1Id = dim.Spell1Id,
                    Spell2Id = dim.Spell2Id,
                    Games = aggregate.Games,
                    Wins = aggregate.Wins
                })
            .ToListAsync(ct);
        return aggregates;
    }

    private static async Task<IReadOnlyList<ChampionAggregateStarterItems>> ProjectStarterItemsAsync(
        TrueMainDbContext db,
        IQueryable<ChampionAggregatePattern> patterns,
        CancellationToken ct)
    {
        var aggregates = await patterns
            .GroupBy(pattern => pattern.StarterItemsId)
            .Select(group => new
            {
                DimId = group.Key,
                Games = group.Sum(pattern => pattern.Games),
                Wins = group.Sum(pattern => pattern.Wins)
            })
            .Join(
                db.ChampionDimStarterItems.AsNoTracking(),
                aggregate => aggregate.DimId,
                dim => dim.Id,
                (aggregate, dim) => new ChampionAggregateStarterItems
                {
                    StarterItemsKey = dim.StarterItemsKey,
                    StarterItems = dim.StarterItems,
                    Games = aggregate.Games,
                    Wins = aggregate.Wins
                })
            .ToListAsync(ct);
        return aggregates;
    }
}
