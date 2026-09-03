using System.Globalization;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ingestor.Processes.Components.PatternAggregation;

/// <summary>
/// Get-or-create for each dimension: load the existing rows that match any requested
/// content, insert the missing ones, return content → ID dictionaries.
///
/// <para>
/// The three dimensions whose identity the schema enforces canonically — rune pages,
/// spell pairs, starter baskets (#1418) — insert with <c>ON CONFLICT DO NOTHING</c> and
/// then re-read, rather than trusting that the row this instance did not find is a row
/// that does not exist. Their content types normalise on construction, so a lookup here
/// and the database's UNIQUE index agree on what "the same row" means; the conflict
/// clause is what keeps a disagreement — a second writer, or a future normalisation bug —
/// costing a re-read instead of a failed aggregation run.
/// </para>
///
/// <para>
/// Builds and skill orders keep the plain insert: their identity is their stored order,
/// nothing normalises, and the aggregator runs single-instance (per Worker.cs's
/// sequential per-process loop) so no second writer can race it.
/// </para>
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

        var existingByContent = await LoadRunesAsync(db, distinctRunes, ct);

        var missing = distinctRunes.Where(content => !existingByContent.ContainsKey(content)).ToList();
        if (missing.Count == 0)
        {
            return existingByContent;
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO champion_dim_rune_pages (
                "Id", "PrimaryStyleId", "PrimaryKeystoneId",
                "PrimaryPerk1Id", "PrimaryPerk2Id", "PrimaryPerk3Id",
                "SecondaryStyleId", "SecondaryPerk1Id", "SecondaryPerk2Id",
                "StatOffense", "StatFlex", "StatDefense")
            SELECT gen_random_uuid(), *
            FROM unnest(
                @primaryStyle::integer[], @keystone::integer[],
                @perk1::integer[], @perk2::integer[], @perk3::integer[],
                @secondaryStyle::integer[], @secondaryPerk1::integer[], @secondaryPerk2::integer[],
                @offense::integer[], @flex::integer[], @defense::integer[])
            ON CONFLICT DO NOTHING
            """,
            [
                new NpgsqlParameter("primaryStyle", missing.Select(c => c.PrimaryStyleId).ToArray()),
                new NpgsqlParameter("keystone", missing.Select(c => c.PrimaryKeystoneId).ToArray()),
                new NpgsqlParameter("perk1", missing.Select(c => c.PrimaryPerk1Id).ToArray()),
                new NpgsqlParameter("perk2", missing.Select(c => c.PrimaryPerk2Id).ToArray()),
                new NpgsqlParameter("perk3", missing.Select(c => c.PrimaryPerk3Id).ToArray()),
                new NpgsqlParameter("secondaryStyle", missing.Select(c => c.SecondaryStyleId).ToArray()),
                new NpgsqlParameter("secondaryPerk1", missing.Select(c => c.SecondaryPerk1Id).ToArray()),
                new NpgsqlParameter("secondaryPerk2", missing.Select(c => c.SecondaryPerk2Id).ToArray()),
                new NpgsqlParameter("offense", missing.Select(c => c.StatOffense).ToArray()),
                new NpgsqlParameter("flex", missing.Select(c => c.StatFlex).ToArray()),
                new NpgsqlParameter("defense", missing.Select(c => c.StatDefense).ToArray())
            ],
            ct);

        return await LoadRunesAsync(db, distinctRunes, ct);
    }

    /// <summary>
    /// Reads the dimension rows that could match any requested page, keyed by content.
    /// Pre-filters on the keystone — the cheapest discriminant — then matches exactly in
    /// memory; the content type sorts the secondary pair, so a row written before the
    /// canonical CHECK existed still lands on the right key.
    /// </summary>
    private static async Task<Dictionary<RunePageDimensionContent, Guid>> LoadRunesAsync(
        TrueMainDbContext db,
        IReadOnlyCollection<RunePageDimensionContent> distinctRunes,
        CancellationToken ct)
    {
        var keystoneIds = distinctRunes.Select(r => r.PrimaryKeystoneId).Distinct().ToList();
        var existing = await db.ChampionDimRunePages
            .AsNoTracking()
            .Where(row => keystoneIds.Contains(row.PrimaryKeystoneId))
            .ToListAsync(ct);

        var byContent = new Dictionary<RunePageDimensionContent, Guid>();
        foreach (var row in existing)
        {
            byContent[new RunePageDimensionContent(
                row.PrimaryStyleId, row.PrimaryKeystoneId,
                row.PrimaryPerk1Id, row.PrimaryPerk2Id, row.PrimaryPerk3Id,
                row.SecondaryStyleId, row.SecondaryPerk1Id, row.SecondaryPerk2Id,
                row.StatOffense, row.StatFlex, row.StatDefense)] = row.Id;
        }

        return byContent;
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

        var existingByContent = await LoadSpellPairsAsync(db, distinctPairs, ct);

        var missing = distinctPairs.Where(content => !existingByContent.ContainsKey(content)).ToList();
        if (missing.Count == 0)
        {
            return existingByContent;
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO champion_dim_spell_pairs ("Id", "Spell1Id", "Spell2Id")
            SELECT gen_random_uuid(), *
            FROM unnest(@spell1::integer[], @spell2::integer[])
            ON CONFLICT DO NOTHING
            """,
            [
                new NpgsqlParameter("spell1", missing.Select(c => c.Spell1Id).ToArray()),
                new NpgsqlParameter("spell2", missing.Select(c => c.Spell2Id).ToArray())
            ],
            ct);

        return await LoadSpellPairsAsync(db, distinctPairs, ct);
    }

    private static async Task<Dictionary<SpellPairDimensionContent, Guid>> LoadSpellPairsAsync(
        TrueMainDbContext db,
        IReadOnlyCollection<SpellPairDimensionContent> distinctPairs,
        CancellationToken ct)
    {
        var spell1Ids = distinctPairs.Select(p => p.Spell1Id).Distinct().ToList();
        var existing = await db.ChampionDimSpellPairs
            .AsNoTracking()
            .Where(row => spell1Ids.Contains(row.Spell1Id))
            .ToListAsync(ct);

        var byContent = new Dictionary<SpellPairDimensionContent, Guid>();
        foreach (var row in existing)
        {
            byContent[new SpellPairDimensionContent(row.Spell1Id, row.Spell2Id)] = row.Id;
        }

        return byContent;
    }

    /// <summary>
    /// Resolves starter baskets by the key Postgres generates for them — item ids
    /// ascending — which <see cref="PatternIntent.StarterItemsKey"/> reproduces exactly.
    /// The stored <c>StarterItems</c> array keeps the analyser's display order, which is
    /// price-dependent and therefore has no business carrying identity (#1418).
    /// </summary>
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
        var existingByKey = await LoadStarterItemsAsync(db, keys, ct);

        var missing = distinctEntries.Where(entry => !existingByKey.ContainsKey(entry.Key)).ToList();
        if (missing.Count == 0)
        {
            return existingByKey;
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO champion_dim_starter_items ("Id", "StarterItems")
            SELECT gen_random_uuid(), basket
            FROM unnest(@baskets::jsonb[]) AS basket
            ON CONFLICT DO NOTHING
            """,
            [new NpgsqlParameter("baskets", missing.Select(entry => ToJsonArray(entry.Items)).ToArray())],
            ct);

        return await LoadStarterItemsAsync(db, keys, ct);
    }

    private static async Task<Dictionary<string, Guid>> LoadStarterItemsAsync(
        TrueMainDbContext db,
        IReadOnlyCollection<string> keys,
        CancellationToken ct)
    {
        var existing = await db.ChampionDimStarterItems
            .AsNoTracking()
            .Where(row => keys.Contains(row.CanonicalKey))
            .Select(row => new { row.CanonicalKey, row.Id })
            .ToListAsync(ct);

        return existing.ToDictionary(row => row.CanonicalKey, row => row.Id, StringComparer.Ordinal);
    }

    /// <summary>
    /// The basket as the JSONB literal the column stores. Hand-built rather than
    /// serialised: the values are integers, and the raw insert has to produce exactly what
    /// the EF mapping would.
    /// </summary>
    private static string ToJsonArray(IReadOnlyList<int> items)
        => "[" + string.Join(",", items.Select(item => item.ToString(CultureInfo.InvariantCulture))) + "]";
}
