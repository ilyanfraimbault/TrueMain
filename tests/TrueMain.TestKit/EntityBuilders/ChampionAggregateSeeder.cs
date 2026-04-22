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
        DateTime aggregatedAtUtc)
        => AddPattern(
            riotAccountId, championId, patch, platformId, queueId, position,
            summoner1Id, summoner2Id, skillOrderKey,
            starterItems: [1055, 2003], starterItemsKey: "1055-2003",
            buildItems, bootsItemId, games, wins, aggregatedAtUtc);

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
        DateTime aggregatedAtUtc)
    {
        var key = new ScopeKey(riotAccountId, championId, patch, platformId, queueId, position);

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

        return this;
    }

    public async Task SaveAsync(DbContext db, CancellationToken ct = default)
    {
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
                Games = accumulator.Games,
                Wins = accumulator.Wins,
                LastGameStartTimeUtc = accumulator.AggregatedAtUtc.AddMinutes(-30),
                AggregatedAtUtc = accumulator.AggregatedAtUtc,
                SpellPairs = accumulator.SpellPairs
                    .Select(pair => new ChampionAggregateSpellPair
                    {
                        Id = Guid.NewGuid(),
                        Spell1Id = pair.Key.spell1,
                        Spell2Id = pair.Key.spell2,
                        Games = pair.Value.Games,
                        Wins = pair.Value.Wins
                    })
                    .ToList(),
                SkillOrders = accumulator.SkillOrders
                    .Select(order => new ChampionAggregateSkillOrder
                    {
                        Id = Guid.NewGuid(),
                        SkillOrderKey = order.Key,
                        Games = order.Value.Games,
                        Wins = order.Value.Wins
                    })
                    .ToList(),
                StarterItems = accumulator.StarterItems
                    .Select(starter => new ChampionAggregateStarterItems
                    {
                        Id = Guid.NewGuid(),
                        StarterItemsKey = starter.Key,
                        StarterItems = starter.Value.Items.ToList(),
                        Games = starter.Value.Games,
                        Wins = starter.Value.Wins
                    })
                    .ToList(),
                Builds = accumulator.Builds
                    .Select(build => new ChampionAggregateBuild
                    {
                        Id = Guid.NewGuid(),
                        BootsItemId = build.Key.BootsItemId,
                        BuildItem0 = build.Key.Item0,
                        BuildItem1 = build.Key.Item1,
                        BuildItem2 = build.Key.Item2,
                        BuildItem3 = build.Key.Item3,
                        BuildItem4 = build.Key.Item4,
                        BuildItem5 = build.Key.Item5,
                        BuildItem6 = build.Key.Item6,
                        Games = build.Value.Games,
                        Wins = build.Value.Wins
                    })
                    .ToList()
            };

            db.Set<ChampionAggregateScope>().Add(scope);
        }

        await db.SaveChangesAsync(ct);
    }

    private readonly record struct ScopeKey(
        Guid RiotAccountId,
        int ChampionId,
        string Patch,
        string PlatformId,
        int QueueId,
        string Position);

    private readonly record struct BuildKey(
        int BootsItemId,
        int Item0, int Item1, int Item2, int Item3,
        int Item4, int Item5, int Item6);

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
