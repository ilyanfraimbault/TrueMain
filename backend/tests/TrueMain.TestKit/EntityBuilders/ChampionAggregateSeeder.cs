using Core.Lol.Ranking;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.TestKit.EntityBuilders;

/// <summary>
/// Helper that folds a stream of "pattern specs" (one per match observed on
/// a champion slice, shaped like the legacy <c>ChampionPatternAggregate</c>
/// test fixture) into the normalised <c>ChampionAggregateScope</c> +
/// dimension rows that the reader side consumes today.
/// </summary>
public sealed class ChampionAggregateSeeder
{
    private readonly Dictionary<ScopeKey, ScopeAccumulator> _scopes = [];

    /// <summary>
    /// Population the next <c>Add*</c> calls seed into. Defaults to mains — the
    /// only population the aggregate held before #1346, and the one every test
    /// written before it describes. Without this default the seeded scopes would
    /// carry <c>IsMain = false</c> (a non-nullable bool is always written, so the
    /// column default never applies) and every mains-only read would see nothing.
    /// </summary>
    private bool _isMain = true;

    /// <summary>
    /// Seeds the following patterns as the non-main population, so a test can
    /// hold both sides of the truemains filter at once. Fluent and sticky: call
    /// it again with <see langword="true"/> to go back to seeding mains.
    /// </summary>
    public ChampionAggregateSeeder WithPopulation(bool isMain)
    {
        _isMain = isMain;
        return this;
    }

    /// <summary>
    /// Convenience overload that uses the default starter-items fixture
    /// ([1055, 2003] with key "1055-2003") used by the legacy tests.
    /// </summary>
    public ChampionAggregateSeeder AddPatternDefaults(
        Guid riotAccountId,
        int championId,
        string patch,
        string platformId,
        int queueId,
        string position,
        int summoner1Id,
        int summoner2Id,
        string skillOrderKey,
        IReadOnlyList<int> buildItems,
        int bootsItemId,
        int games,
        int wins,
        DateTime aggregatedAtUtc,
        string eloBracket = EloBracket.Gold)
        => AddPattern(
            riotAccountId, championId, patch, platformId, queueId, position,
            summoner1Id, summoner2Id, skillOrderKey,
            starterItems: [1055, 2003], starterItemsKey: "1055-2003",
            buildItems, bootsItemId, games, wins, aggregatedAtUtc, eloBracket);

    /// <summary>
    /// Convenience overload that pins a specific rune page (primary keystone
    /// + secondary tree). Used by tests that need (firstItem, keystone)
    /// grouping in the new champion builds endpoint.
    /// </summary>
    public ChampionAggregateSeeder AddPatternWithRune(
        Guid riotAccountId,
        int championId,
        string patch,
        string platformId,
        int queueId,
        string position,
        int summoner1Id,
        int summoner2Id,
        string skillOrderKey,
        IReadOnlyList<int> buildItems,
        int bootsItemId,
        int primaryStyleId,
        int primaryKeystoneId,
        int secondaryStyleId,
        int games,
        int wins,
        DateTime aggregatedAtUtc,
        string eloBracket = EloBracket.Gold)
        => AddPattern(
            riotAccountId, championId, patch, platformId, queueId, position,
            summoner1Id, summoner2Id, skillOrderKey,
            starterItems: [1055, 2003], starterItemsKey: "1055-2003",
            buildItems, bootsItemId,
            new RunePageKey(primaryStyleId, primaryKeystoneId, secondaryStyleId),
            games, wins, aggregatedAtUtc, eloBracket);

    public ChampionAggregateSeeder AddPattern(
        Guid riotAccountId,
        int championId,
        string patch,
        string platformId,
        int queueId,
        string position,
        int summoner1Id,
        int summoner2Id,
        string skillOrderKey,
        IReadOnlyList<int> starterItems,
        string starterItemsKey,
        IReadOnlyList<int> buildItems,
        int bootsItemId,
        int games,
        int wins,
        DateTime aggregatedAtUtc,
        string eloBracket = EloBracket.Gold)
        => AddPattern(
            riotAccountId, championId, patch, platformId, queueId, position,
            summoner1Id, summoner2Id, skillOrderKey,
            starterItems, starterItemsKey,
            buildItems, bootsItemId,
            RunePageKey.Placeholder,
            games, wins, aggregatedAtUtc, eloBracket);

    private ChampionAggregateSeeder AddPattern(
        Guid riotAccountId,
        int championId,
        string patch,
        string platformId,
        int queueId,
        string position,
        int summoner1Id,
        int summoner2Id,
        string skillOrderKey,
        IReadOnlyList<int> starterItems,
        string starterItemsKey,
        IReadOnlyList<int> buildItems,
        int bootsItemId,
        RunePageKey runePageKey,
        int games,
        int wins,
        DateTime aggregatedAtUtc,
        string eloBracket = EloBracket.Gold)
    {
        var key = new ScopeKey(
            riotAccountId, championId, patch, platformId, queueId, position, eloBracket, _isMain);

        if (!_scopes.TryGetValue(key, out var acc))
        {
            acc = new ScopeAccumulator(key, aggregatedAtUtc);
            _scopes[key] = acc;
        }

        acc.Observe(aggregatedAtUtc);
        acc.Games += games;
        acc.Wins += wins;

        acc.SpellPairs.TryGetValue((summoner1Id, summoner2Id), out var spell);
        spell.Games += games;
        spell.Wins += wins;
        acc.SpellPairs[(summoner1Id, summoner2Id)] = spell;

        acc.SkillOrders.TryGetValue(skillOrderKey, out var skill);
        skill.Games += games;
        skill.Wins += wins;
        acc.SkillOrders[skillOrderKey] = skill;

        if (!acc.StarterItems.TryGetValue(starterItemsKey, out var starter))
        {
            starter = new StarterAccumulator(starterItems);
        }
        starter.Games += games;
        starter.Wins += wins;
        acc.StarterItems[starterItemsKey] = starter;

        var buildKey = new BuildKey(
            bootsItemId,
            buildItems.ElementAtOrDefault(0), buildItems.ElementAtOrDefault(1),
            buildItems.ElementAtOrDefault(2), buildItems.ElementAtOrDefault(3),
            buildItems.ElementAtOrDefault(4), buildItems.ElementAtOrDefault(5),
            buildItems.ElementAtOrDefault(6));
        acc.Builds.TryGetValue(buildKey, out var build);
        build.Games += games;
        build.Wins += wins;
        acc.Builds[buildKey] = build;

        // Phase 6 — track the full combo (build + skill + spells + starters
        // + rune page) so SaveAsync can emit one pattern row per observed
        // tuple. The rune-page slot defaults to a single placeholder when
        // callers don't pin one.
        var patternKey = new PatternKey(buildKey, skillOrderKey, summoner1Id, summoner2Id, starterItemsKey, runePageKey);
        acc.Patterns.TryGetValue(patternKey, out var pattern);
        pattern.Games += games;
        pattern.Wins += wins;
        acc.Patterns[patternKey] = pattern;

        return this;
    }

    public async Task SaveAsync(DbContext db, CancellationToken ct = default)
    {
        // Phase 6 dim cache: dedup rows across all scopes for the rest of this
        // save call so we mirror the global-deduplication semantics of the
        // production aggregator.
        var dimBuilds = new Dictionary<BuildKey, ChampionDimBuild>();
        var dimSkillOrders = new Dictionary<string, ChampionDimSkillOrder>(StringComparer.Ordinal);
        var dimSpellPairs = new Dictionary<(int Spell1, int Spell2), ChampionDimSpellPair>();
        var dimStarterItems = new Dictionary<string, ChampionDimStarterItems>(StringComparer.Ordinal);

        // Dedupe rune pages globally across this save so the same
        // (style, keystone, secondary) shape maps to a single FK target.
        // Tests that don't pin a rune page fall back to a single placeholder
        // (RunePageKey.Placeholder = all zeros).
        var dimRunePages = new Dictionary<RunePageKey, ChampionDimRunePage>();

        foreach (var accumulator in _scopes.Values)
        {
            var scope = new ChampionAggregateScope
            {
                Id = Guid.NewGuid(),
                RiotAccountId = accumulator.Key.RiotAccountId,
                ChampionId = accumulator.Key.ChampionId,
                GameVersion = accumulator.Key.Patch,
                PlatformId = accumulator.Key.PlatformId,
                QueueId = accumulator.Key.QueueId,
                Position = accumulator.Key.Position,
                EloBracket = accumulator.Key.EloBracket,
                IsMain = accumulator.Key.IsMain,
                Games = accumulator.Games,
                Wins = accumulator.Wins,
                LastGameStartTimeUtc = accumulator.AggregatedAtUtc.AddMinutes(-30),
                AggregatedAtUtc = accumulator.AggregatedAtUtc
            };

            db.Set<ChampionAggregateScope>().Add(scope);

            // Phase 6 — emit dim rows + pattern rows for the same scope.
            // Dim entries are deduplicated globally via the per-save caches
            // declared above so a re-used build/skill/spell/starter across
            // scopes maps to a single FK target.
            foreach (var (patternKey, counter) in accumulator.Patterns)
            {
                var dimBuild = GetOrAddDimBuild(db, dimBuilds, patternKey.Build);
                var dimSkillOrder = GetOrAddDimSkillOrder(db, dimSkillOrders, patternKey.SkillOrderKey);
                var dimSpellPair = GetOrAddDimSpellPair(db, dimSpellPairs, (patternKey.Spell1Id, patternKey.Spell2Id));
                var dimStarter = GetOrAddDimStarterItems(
                    db,
                    cache: dimStarterItems,
                    starterItemsKey: patternKey.StarterItemsKey,
                    items: accumulator.StarterItems[patternKey.StarterItemsKey].Items);

                var dimRunePage = GetOrAddDimRunePage(db, dimRunePages, patternKey.RunePage);

                db.Set<ChampionAggregatePattern>().Add(new ChampionAggregatePattern
                {
                    ScopeId = scope.Id,
                    BuildId = dimBuild.Id,
                    RunePageId = dimRunePage.Id,
                    SkillOrderId = dimSkillOrder.Id,
                    SpellPairId = dimSpellPair.Id,
                    StarterItemsId = dimStarter.Id,
                    Games = counter.Games,
                    Wins = counter.Wins
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static ChampionDimBuild GetOrAddDimBuild(
        DbContext db,
        Dictionary<BuildKey, ChampionDimBuild> cache,
        BuildKey key)
    {
        if (cache.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var row = new ChampionDimBuild
        {
            BootsItemId = key.BootsItemId,
            BuildItem0 = key.Item0, BuildItem1 = key.Item1, BuildItem2 = key.Item2,
            BuildItem3 = key.Item3, BuildItem4 = key.Item4, BuildItem5 = key.Item5,
            BuildItem6 = key.Item6
        };
        db.Set<ChampionDimBuild>().Add(row);
        cache[key] = row;
        return row;
    }

    private static ChampionDimSkillOrder GetOrAddDimSkillOrder(
        DbContext db,
        Dictionary<string, ChampionDimSkillOrder> cache,
        string skillOrderKey)
    {
        if (cache.TryGetValue(skillOrderKey, out var existing))
        {
            return existing;
        }

        var row = new ChampionDimSkillOrder { SkillOrderKey = skillOrderKey };
        db.Set<ChampionDimSkillOrder>().Add(row);
        cache[skillOrderKey] = row;
        return row;
    }

    /// <summary>
    /// Sorts the pair before storing it, the way the ingestor does and the way the
    /// dimension's CHECK requires (#1418): a loadout is a set, so a test that passes
    /// (Ignite, Flash) is asking for the same row as one that passes (Flash, Ignite).
    /// </summary>
    private static ChampionDimSpellPair GetOrAddDimSpellPair(
        DbContext db,
        Dictionary<(int Spell1, int Spell2), ChampionDimSpellPair> cache,
        (int Spell1, int Spell2) pair)
    {
        var key = (Spell1: Math.Min(pair.Spell1, pair.Spell2), Spell2: Math.Max(pair.Spell1, pair.Spell2));
        if (cache.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var row = new ChampionDimSpellPair { Spell1Id = key.Spell1, Spell2Id = key.Spell2 };
        db.Set<ChampionDimSpellPair>().Add(row);
        cache[key] = row;
        return row;
    }

    private static ChampionDimStarterItems GetOrAddDimStarterItems(
        DbContext db,
        Dictionary<string, ChampionDimStarterItems> cache,
        string starterItemsKey,
        IReadOnlyList<int> items)
    {
        if (cache.TryGetValue(starterItemsKey, out var existing))
        {
            return existing;
        }

        // No key to set: Postgres generates it from the basket (#1418). The cache key is
        // still the caller's, so a seeder that hands out two spellings of one basket now
        // hits the database's UNIQUE index — which is the point.
        var row = new ChampionDimStarterItems { StarterItems = items.ToList() };
        db.Set<ChampionDimStarterItems>().Add(row);
        cache[starterItemsKey] = row;
        return row;
    }

    private readonly record struct ScopeKey(
        Guid RiotAccountId,
        int ChampionId,
        string Patch,
        string PlatformId,
        int QueueId,
        string Position,
        string EloBracket,
        bool IsMain);

    private readonly record struct BuildKey(
        int BootsItemId,
        int Item0, int Item1, int Item2, int Item3,
        int Item4, int Item5, int Item6);

    private readonly record struct RunePageKey(
        int PrimaryStyleId,
        int PrimaryKeystoneId,
        int SecondaryStyleId)
    {
        public static RunePageKey Placeholder { get; } = new(0, 0, 0);
    }

    private readonly record struct PatternKey(
        BuildKey Build,
        string SkillOrderKey,
        int Spell1Id,
        int Spell2Id,
        string StarterItemsKey,
        RunePageKey RunePage);

    private static ChampionDimRunePage GetOrAddDimRunePage(
        DbContext db,
        Dictionary<RunePageKey, ChampionDimRunePage> cache,
        RunePageKey key)
    {
        if (cache.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var row = new ChampionDimRunePage
        {
            PrimaryStyleId = key.PrimaryStyleId,
            PrimaryKeystoneId = key.PrimaryKeystoneId,
            PrimaryPerk1Id = 0, PrimaryPerk2Id = 0, PrimaryPerk3Id = 0,
            SecondaryStyleId = key.SecondaryStyleId,
            SecondaryPerk1Id = 0, SecondaryPerk2Id = 0,
            StatOffense = 0, StatFlex = 0, StatDefense = 0
        };
        db.Set<ChampionDimRunePage>().Add(row);
        cache[key] = row;
        return row;
    }

    private sealed class ScopeAccumulator(ScopeKey key, DateTime aggregatedAtUtc)
    {
        public ScopeKey Key { get; } = key;
        public DateTime AggregatedAtUtc { get; private set; } = aggregatedAtUtc;
        public int Games { get; set; }
        public int Wins { get; set; }

        public Dictionary<(int spell1, int spell2), DimCounter> SpellPairs { get; } = [];
        public Dictionary<string, DimCounter> SkillOrders { get; } = [];
        public Dictionary<string, StarterAccumulator> StarterItems { get; } = [];
        public Dictionary<BuildKey, DimCounter> Builds { get; } = [];
        public Dictionary<PatternKey, DimCounter> Patterns { get; } = [];

        public void Observe(DateTime aggregatedAtUtc)
        {
            if (aggregatedAtUtc > AggregatedAtUtc)
            {
                AggregatedAtUtc = aggregatedAtUtc;
            }
        }
    }

    private struct DimCounter
    {
        public int Games;
        public int Wins;
    }

    private struct StarterAccumulator(IReadOnlyList<int> items)
    {
        public IReadOnlyList<int> Items { get; } = items;
        public int Games;
        public int Wins;
    }
}
