using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes.Components.PatternAggregation;

/// <summary>
/// Single-instance get-or-create implementation: for each dimension,
/// load existing rows that match any of the requested contents, insert
/// the missing ones, return content → ID dictionaries. The aggregator
/// runs single-instance today (per Worker.cs's sequential per-process
/// loop), so we don't need <c>INSERT ... ON CONFLICT DO NOTHING</c>
/// race-safe semantics yet — when 6.2 graduates to multi-instance, swap
/// the simple insert for an upsert + re-select cycle.
/// </summary>
public sealed class ChampionDimensionResolver(
    IDbContextFactory<TrueMainDbContext> dbContextFactory) : IChampionDimensionResolver
{
    public async Task<DimensionResolution> ResolveAsync(
        IReadOnlyCollection<PatternIntent> patterns,
        CancellationToken ct)
    {
        var distinctBuilds = patterns.Select(p => p.Build).Distinct().ToList();
        var distinctRunes = patterns.Select(p => p.RunePage).Distinct().ToList();
        var distinctSkillOrders = patterns.Select(p => p.SkillOrderKey).Distinct(StringComparer.Ordinal).ToList();
        var distinctSpellPairs = patterns.Select(p => p.SpellPair).Distinct().ToList();
        var distinctStarterItems = patterns
            .Select(p => (p.StarterItemsKey, p.StarterItems))
            .DistinctBy(entry => entry.StarterItemsKey, StringComparer.Ordinal)
            .ToList();

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var builds = await ResolveBuildsAsync(db, distinctBuilds, ct);
        var runes = await ResolveRunesAsync(db, distinctRunes, ct);
        var skillOrders = await ResolveSkillOrdersAsync(db, distinctSkillOrders, ct);
        var spellPairs = await ResolveSpellPairsAsync(db, distinctSpellPairs, ct);
        var starterItems = await ResolveStarterItemsAsync(db, distinctStarterItems, ct);

        return new DimensionResolution(builds, runes, skillOrders, spellPairs, starterItems);
    }

    private static async Task<Dictionary<BuildDimensionContent, Guid>> ResolveBuildsAsync(
        TrueMainDbContext db,
        IReadOnlyCollection<BuildDimensionContent> distinctBuilds,
        CancellationToken ct)
    {
        if (distinctBuilds.Count == 0)
        {
            return [];
        }

        // Pre-filter on a high-cardinality column (BootsItemId is the cheapest
        // discriminant for builds) then exact-match in memory. Keeps the SQL
        // simple while bounding the row set we read.
        var bootIds = distinctBuilds.Select(b => b.BootsItemId).Distinct().ToList();
        var existing = await db.ChampionDimBuilds
            .AsNoTracking()
            .Where(row => bootIds.Contains(row.BootsItemId))
            .ToListAsync(ct);

        var existingByContent = existing.ToDictionary(
            row => new BuildDimensionContent(
                row.BootsItemId, row.BuildItem0, row.BuildItem1, row.BuildItem2,
                row.BuildItem3, row.BuildItem4, row.BuildItem5, row.BuildItem6),
            row => row.Id);

        var missing = distinctBuilds.Where(content => !existingByContent.ContainsKey(content)).ToList();
        if (missing.Count == 0)
        {
            return existingByContent;
        }

        var newRows = missing.Select(content => new ChampionDimBuild
        {
            BootsItemId = content.BootsItemId,
            BuildItem0 = content.BuildItem0,
            BuildItem1 = content.BuildItem1,
            BuildItem2 = content.BuildItem2,
            BuildItem3 = content.BuildItem3,
            BuildItem4 = content.BuildItem4,
            BuildItem5 = content.BuildItem5,
            BuildItem6 = content.BuildItem6
        }).ToList();
        db.ChampionDimBuilds.AddRange(newRows);
        await db.SaveChangesAsync(ct);

        foreach (var (content, row) in missing.Zip(newRows))
        {
            existingByContent[content] = row.Id;
        }
        return existingByContent;
    }

    private static async Task<Dictionary<RunePageDimensionContent, Guid>> ResolveRunesAsync(
        TrueMainDbContext db,
        IReadOnlyCollection<RunePageDimensionContent> distinctRunes,
        CancellationToken ct)
    {
        if (distinctRunes.Count == 0)
        {
            return [];
        }

        var keystoneIds = distinctRunes.Select(r => r.PrimaryKeystoneId).Distinct().ToList();
        var existing = await db.ChampionDimRunePages
            .AsNoTracking()
            .Where(row => keystoneIds.Contains(row.PrimaryKeystoneId))
            .ToListAsync(ct);

        var existingByContent = existing.ToDictionary(
            row => new RunePageDimensionContent(
                row.PrimaryStyleId, row.PrimaryKeystoneId,
                row.PrimaryPerk1Id, row.PrimaryPerk2Id, row.PrimaryPerk3Id,
                row.SecondaryStyleId, row.SecondaryPerk1Id, row.SecondaryPerk2Id,
                row.StatOffense, row.StatFlex, row.StatDefense),
            row => row.Id);

        var missing = distinctRunes.Where(content => !existingByContent.ContainsKey(content)).ToList();
        if (missing.Count == 0)
        {
            return existingByContent;
        }

        var newRows = missing.Select(content => new ChampionDimRunePage
        {
            PrimaryStyleId = content.PrimaryStyleId,
            PrimaryKeystoneId = content.PrimaryKeystoneId,
            PrimaryPerk1Id = content.PrimaryPerk1Id,
            PrimaryPerk2Id = content.PrimaryPerk2Id,
            PrimaryPerk3Id = content.PrimaryPerk3Id,
            SecondaryStyleId = content.SecondaryStyleId,
            SecondaryPerk1Id = content.SecondaryPerk1Id,
            SecondaryPerk2Id = content.SecondaryPerk2Id,
            StatOffense = content.StatOffense,
            StatFlex = content.StatFlex,
            StatDefense = content.StatDefense
        }).ToList();
        db.ChampionDimRunePages.AddRange(newRows);
        await db.SaveChangesAsync(ct);

        foreach (var (content, row) in missing.Zip(newRows))
        {
            existingByContent[content] = row.Id;
        }
        return existingByContent;
    }

    private static async Task<Dictionary<string, Guid>> ResolveSkillOrdersAsync(
        TrueMainDbContext db,
        IReadOnlyCollection<string> distinctKeys,
        CancellationToken ct)
    {
        if (distinctKeys.Count == 0)
        {
            return new Dictionary<string, Guid>(StringComparer.Ordinal);
        }

        var existing = await db.ChampionDimSkillOrders
            .AsNoTracking()
            .Where(row => distinctKeys.Contains(row.SkillOrderKey))
            .ToListAsync(ct);

        var existingByKey = existing.ToDictionary(
            row => row.SkillOrderKey,
            row => row.Id,
            StringComparer.Ordinal);

        var missing = distinctKeys.Where(key => !existingByKey.ContainsKey(key)).ToList();
        if (missing.Count == 0)
        {
            return existingByKey;
        }

        var newRows = missing.Select(key => new ChampionDimSkillOrder { SkillOrderKey = key }).ToList();
        db.ChampionDimSkillOrders.AddRange(newRows);
        await db.SaveChangesAsync(ct);

        foreach (var (key, row) in missing.Zip(newRows))
        {
            existingByKey[key] = row.Id;
        }
        return existingByKey;
    }

    private static async Task<Dictionary<SpellPairDimensionContent, Guid>> ResolveSpellPairsAsync(
        TrueMainDbContext db,
        IReadOnlyCollection<SpellPairDimensionContent> distinctPairs,
        CancellationToken ct)
    {
        if (distinctPairs.Count == 0)
        {
            return [];
        }

        var spell1Ids = distinctPairs.Select(p => p.Spell1Id).Distinct().ToList();
        var existing = await db.ChampionDimSpellPairs
            .AsNoTracking()
            .Where(row => spell1Ids.Contains(row.Spell1Id))
            .ToListAsync(ct);

        var existingByContent = existing.ToDictionary(
            row => new SpellPairDimensionContent(row.Spell1Id, row.Spell2Id),
            row => row.Id);

        var missing = distinctPairs.Where(content => !existingByContent.ContainsKey(content)).ToList();
        if (missing.Count == 0)
        {
            return existingByContent;
        }

        var newRows = missing.Select(content => new ChampionDimSpellPair
        {
            Spell1Id = content.Spell1Id,
            Spell2Id = content.Spell2Id
        }).ToList();
        db.ChampionDimSpellPairs.AddRange(newRows);
        await db.SaveChangesAsync(ct);

        foreach (var (content, row) in missing.Zip(newRows))
        {
            existingByContent[content] = row.Id;
        }
        return existingByContent;
    }

    private static async Task<Dictionary<string, Guid>> ResolveStarterItemsAsync(
        TrueMainDbContext db,
        IReadOnlyCollection<(string Key, IReadOnlyList<int> Items)> distinctEntries,
        CancellationToken ct)
    {
        if (distinctEntries.Count == 0)
        {
            return new Dictionary<string, Guid>(StringComparer.Ordinal);
        }

        var keys = distinctEntries.Select(entry => entry.Key).ToList();
        var existing = await db.ChampionDimStarterItems
            .AsNoTracking()
            .Where(row => keys.Contains(row.StarterItemsKey))
            .ToListAsync(ct);

        var existingByKey = existing.ToDictionary(
            row => row.StarterItemsKey,
            row => row.Id,
            StringComparer.Ordinal);

        var missing = distinctEntries.Where(entry => !existingByKey.ContainsKey(entry.Key)).ToList();
        if (missing.Count == 0)
        {
            return existingByKey;
        }

        var newRows = missing.Select(entry => new ChampionDimStarterItems
        {
            StarterItemsKey = entry.Key,
            StarterItems = entry.Items.ToList()
        }).ToList();
        db.ChampionDimStarterItems.AddRange(newRows);
        await db.SaveChangesAsync(ct);

        foreach (var (entry, row) in missing.Zip(newRows))
        {
            existingByKey[entry.Key] = row.Id;
        }
        return existingByKey;
    }
}
