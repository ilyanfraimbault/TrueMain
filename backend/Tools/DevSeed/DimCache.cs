using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevSeed;

/// <summary>
/// Get-or-create cache over the globally-deduplicated <c>champion_dim_*</c>
/// tables, keyed by content. Every dim table carries a unique index on its
/// content (see <c>ChampionDim*Configuration</c>), and many champions share the
/// same archetype (identical items/skill order/spells) — inserting a fresh row
/// per champion would violate those constraints on the second champion of any
/// archetype. Loads existing rows once at startup (so reruns of the tool, which
/// never delete dim rows, reuse rather than duplicate them) and adds new ones to
/// the context as they're first seen.
/// </summary>
public sealed class DimCache(TrueMainDbContext db)
{
    private readonly Dictionary<BuildKey, Guid> _builds = [];
    private readonly Dictionary<RunePageKey, Guid> _runePages = [];
    private readonly Dictionary<string, Guid> _skillOrders = [];
    private readonly Dictionary<(int, int), Guid> _spellPairs = [];
    private readonly Dictionary<string, Guid> _starterItems = [];

    public async Task InitializeAsync()
    {
        foreach (var b in await db.ChampionDimBuilds.AsNoTracking().ToListAsync())
        {
            _builds[new BuildKey(b.BootsItemId, b.BuildItem0, b.BuildItem1, b.BuildItem2, b.BuildItem3, b.BuildItem4, b.BuildItem5, b.BuildItem6)] = b.Id;
        }

        foreach (var r in await db.ChampionDimRunePages.AsNoTracking().ToListAsync())
        {
            _runePages[new RunePageKey(r.PrimaryStyleId, r.PrimaryKeystoneId, r.PrimaryPerk1Id, r.PrimaryPerk2Id, r.PrimaryPerk3Id,
                r.SecondaryStyleId, r.SecondaryPerk1Id, r.SecondaryPerk2Id, r.StatOffense, r.StatFlex, r.StatDefense)] = r.Id;
        }

        foreach (var s in await db.ChampionDimSkillOrders.AsNoTracking().ToListAsync())
        {
            _skillOrders[s.SkillOrderKey] = s.Id;
        }

        foreach (var sp in await db.ChampionDimSpellPairs.AsNoTracking().ToListAsync())
        {
            _spellPairs[(sp.Spell1Id, sp.Spell2Id)] = sp.Id;
        }

        foreach (var si in await db.ChampionDimStarterItems.AsNoTracking().ToListAsync())
        {
            _starterItems[si.StarterItemsKey] = si.Id;
        }
    }

    public Guid GetOrAddBuild(int bootsItemId, int[] items)
    {
        var key = new BuildKey(bootsItemId,
            items.ElementAtOrDefault(0), items.ElementAtOrDefault(1), items.ElementAtOrDefault(2), items.ElementAtOrDefault(3),
            items.ElementAtOrDefault(4), items.ElementAtOrDefault(5), items.ElementAtOrDefault(6));
        if (_builds.TryGetValue(key, out var id))
        {
            return id;
        }

        var row = new ChampionDimBuild
        {
            Id = Guid.NewGuid(),
            BootsItemId = key.BootsItemId,
            BuildItem0 = key.Item0,
            BuildItem1 = key.Item1,
            BuildItem2 = key.Item2,
            BuildItem3 = key.Item3,
            BuildItem4 = key.Item4,
            BuildItem5 = key.Item5,
            BuildItem6 = key.Item6,
        };
        db.ChampionDimBuilds.Add(row);
        _builds[key] = row.Id;
        return row.Id;
    }

    public Guid GetOrAddRunePage(
        int primaryStyleId, int primaryKeystoneId, int perk1, int perk2, int perk3,
        int secondaryStyleId, int secondaryPerk1, int secondaryPerk2,
        int statOffense, int statFlex, int statDefense)
    {
        var key = new RunePageKey(primaryStyleId, primaryKeystoneId, perk1, perk2, perk3, secondaryStyleId, secondaryPerk1, secondaryPerk2, statOffense, statFlex, statDefense);
        if (_runePages.TryGetValue(key, out var id))
        {
            return id;
        }

        var row = new ChampionDimRunePage
        {
            Id = Guid.NewGuid(),
            PrimaryStyleId = key.PrimaryStyleId,
            PrimaryKeystoneId = key.PrimaryKeystoneId,
            PrimaryPerk1Id = key.Perk1,
            PrimaryPerk2Id = key.Perk2,
            PrimaryPerk3Id = key.Perk3,
            SecondaryStyleId = key.SecondaryStyleId,
            SecondaryPerk1Id = key.SecondaryPerk1,
            SecondaryPerk2Id = key.SecondaryPerk2,
            StatOffense = key.StatOffense,
            StatFlex = key.StatFlex,
            StatDefense = key.StatDefense,
        };
        db.ChampionDimRunePages.Add(row);
        _runePages[key] = row.Id;
        return row.Id;
    }

    public Guid GetOrAddSkillOrder(string[] sequence)
    {
        var key = string.Join('-', sequence);
        if (_skillOrders.TryGetValue(key, out var id))
        {
            return id;
        }

        var row = new ChampionDimSkillOrder { Id = Guid.NewGuid(), SkillOrderKey = key };
        db.ChampionDimSkillOrders.Add(row);
        _skillOrders[key] = row.Id;
        return row.Id;
    }

    public Guid GetOrAddSpellPair(int spell1, int spell2)
    {
        var key = (spell1, spell2);
        if (_spellPairs.TryGetValue(key, out var id))
        {
            return id;
        }

        var row = new ChampionDimSpellPair { Id = Guid.NewGuid(), Spell1Id = spell1, Spell2Id = spell2 };
        db.ChampionDimSpellPairs.Add(row);
        _spellPairs[key] = row.Id;
        return row.Id;
    }

    public Guid GetOrAddStarterItems(int[] items)
    {
        var key = string.Join('-', items);
        if (_starterItems.TryGetValue(key, out var id))
        {
            return id;
        }

        var row = new ChampionDimStarterItems { Id = Guid.NewGuid(), StarterItemsKey = key, StarterItems = items.ToList() };
        db.ChampionDimStarterItems.Add(row);
        _starterItems[key] = row.Id;
        return row.Id;
    }

    private readonly record struct BuildKey(int BootsItemId, int Item0, int Item1, int Item2, int Item3, int Item4, int Item5, int Item6);

    private readonly record struct RunePageKey(
        int PrimaryStyleId, int PrimaryKeystoneId, int Perk1, int Perk2, int Perk3,
        int SecondaryStyleId, int SecondaryPerk1, int SecondaryPerk2,
        int StatOffense, int StatFlex, int StatDefense);
}
