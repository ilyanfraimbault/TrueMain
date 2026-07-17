using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Pure win-weighted aggregation of per-participant build facts into a
/// <see cref="CompositionBuildRecommendation"/>. Each game votes for its
/// choice on every dimension it has data for; winning games vote with
/// <c>winWeight</c>, losses with 1. The weights only pick the winner —
/// the reported Games / PickRate / WinRate stay raw counts so the numbers
/// remain honest.
/// </summary>
public static class CompositionBuildAggregator
{
    /// <summary>Core path depth: the first completed legendaries that define a build.</summary>
    private const int CorePathLength = 3;

    public static CompositionBuildRecommendation Aggregate(
        IReadOnlyList<CompositionParticipantFacts> facts,
        double winWeight,
        int situationalItemCount)
    {
        var totalGames = facts.Count;
        if (totalGames == 0)
        {
            return new CompositionBuildRecommendation();
        }

        var corePath = PickTop(
            facts.Where(f => f.BuildItems.Count > 0),
            f => (IReadOnlyList<int>)f.BuildItems.Take(CorePathLength).ToArray(),
            winWeight,
            comparer: ItemListComparer.Instance);
        var corePathItems = corePath?.Key ?? [];

        return new CompositionBuildRecommendation
        {
            GamesConsidered = totalGames,
            Wins = facts.Count(f => f.Win),
            RunePage = PickTop(facts.Where(f => f.RunePage is not null), f => f.RunePage!, winWeight)
                is { } runes
                ? ToRunePageReadModel(runes.Key, runes.Games, runes.Wins, totalGames)
                : null,
            StarterItems = PickTop(
                    facts.Where(f => f.StarterItems.Count > 0),
                    f => (IReadOnlyList<int>)f.StarterItems,
                    winWeight,
                    comparer: ItemListComparer.Instance)
                is { } starter
                ? ToItemSet(starter.Key, starter.Games, starter.Wins, totalGames)
                : null,
            Boots = PickTop(facts.Where(f => f.BootsItemId > 0), f => f.BootsItemId, winWeight)
                is { } boots
                ? ToItemSet([boots.Key], boots.Games, boots.Wins, totalGames)
                : null,
            CorePath = corePath is null
                ? null
                : new BuildItemPathReadModel
                {
                    ItemIds = corePathItems,
                    Games = corePath.Games,
                    PickRate = RateMath.Rate(corePath.Games, totalGames),
                    WinRate = RateMath.Rate(corePath.Wins, corePath.Games),
                },
            SituationalItems = AggregateSituationalItems(
                facts, corePathItems, winWeight, situationalItemCount, totalGames),
            SummonerSpells = PickTop(
                    facts.Where(f => f.Spell1Id > 0 || f.Spell2Id > 0),
                    f => (f.Spell1Id, f.Spell2Id),
                    winWeight)
                is { } spells
                ? new BuildSummonerSpellsReadModel
                {
                    Spell1Id = spells.Key.Spell1Id,
                    Spell2Id = spells.Key.Spell2Id,
                    Games = spells.Games,
                    PickRate = RateMath.Rate(spells.Games, totalGames),
                    WinRate = RateMath.Rate(spells.Wins, spells.Games),
                }
                : null,
            SkillOrder = PickTop(
                    facts.Where(f => f.SkillOrderKey.Length > 0),
                    f => f.SkillOrderKey,
                    winWeight)
                is { } skills
                ? new BuildSkillOrderReadModel
                {
                    Sequence = skills.Key.Split('-'),
                    Games = skills.Games,
                    PickRate = RateMath.Rate(skills.Games, totalGames),
                    WinRate = RateMath.Rate(skills.Wins, skills.Games),
                }
                : null,
        };
    }

    /// <summary>
    /// Completed items outside the core path, ranked by win-weighted votes. A
    /// game votes at most once per item, so a duplicated legendary cannot
    /// inflate its own support.
    /// </summary>
    private static List<BuildItemSetReadModel> AggregateSituationalItems(
        IReadOnlyList<CompositionParticipantFacts> facts,
        IReadOnlyList<int> corePathItems,
        double winWeight,
        int situationalItemCount,
        int totalGames)
    {
        var core = corePathItems.ToHashSet();
        var votes = facts
            .SelectMany(f => f.BuildItems
                .Where(item => !core.Contains(item))
                .Distinct()
                .Select(item => (Item: item, f.Win)))
            .GroupBy(x => x.Item)
            .Select(g => new
            {
                Item = g.Key,
                Games = g.Count(),
                Wins = g.Count(x => x.Win),
                Weight = g.Sum(x => x.Win ? winWeight : 1d),
            })
            .OrderByDescending(x => x.Weight)
            .ThenByDescending(x => x.Games)
            .ThenBy(x => x.Item)
            .Take(situationalItemCount);

        return votes
            .Select(x => ToItemSet([x.Item], x.Games, x.Wins, totalGames))
            .ToList();
    }

    private sealed record TopGroup<TKey>(TKey Key, int Games, int Wins);

    /// <summary>
    /// Groups the eligible facts by <paramref name="keySelector"/> and returns
    /// the group with the highest win-weighted vote (ties: raw games, then
    /// insertion order). Null when nothing was eligible.
    /// </summary>
    private static TopGroup<TKey>? PickTop<TKey>(
        IEnumerable<CompositionParticipantFacts> eligible,
        Func<CompositionParticipantFacts, TKey> keySelector,
        double winWeight,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        return eligible
            .GroupBy(keySelector, comparer)
            .Select(g => new
            {
                g.Key,
                Games = g.Count(),
                Wins = g.Count(f => f.Win),
                Weight = g.Sum(f => f.Win ? winWeight : 1d),
            })
            .OrderByDescending(x => x.Weight)
            .ThenByDescending(x => x.Games)
            .Select(x => new TopGroup<TKey>(x.Key, x.Games, x.Wins))
            .FirstOrDefault();
    }

    private static BuildItemSetReadModel ToItemSet(
        IReadOnlyList<int> itemIds, int games, int wins, int totalGames)
        => new()
        {
            ItemIds = itemIds,
            Games = games,
            PickRate = RateMath.Rate(games, totalGames),
            WinRate = RateMath.Rate(wins, games),
        };

    private static BuildRunePageReadModel ToRunePageReadModel(
        CompositionRunePageFacts page, int games, int wins, int totalGames)
        => new()
        {
            PrimaryStyleId = page.PrimaryStyleId,
            PrimaryKeystoneId = page.PrimaryKeystoneId,
            PrimaryPerk1Id = page.PrimaryPerk1Id,
            PrimaryPerk2Id = page.PrimaryPerk2Id,
            PrimaryPerk3Id = page.PrimaryPerk3Id,
            SecondaryStyleId = page.SecondaryStyleId,
            SecondaryPerk1Id = page.SecondaryPerk1Id,
            SecondaryPerk2Id = page.SecondaryPerk2Id,
            StatOffense = page.StatOffense,
            StatFlex = page.StatFlex,
            StatDefense = page.StatDefense,
            Games = games,
            PickRate = RateMath.Rate(games, totalGames),
            WinRate = RateMath.Rate(wins, games),
        };

    /// <summary>
    /// Sequence-equality comparer so identical item lists group together
    /// (arrays and lists compare by reference otherwise).
    /// </summary>
    private sealed class ItemListComparer : IEqualityComparer<IReadOnlyList<int>>
    {
        public static readonly ItemListComparer Instance = new();

        public bool Equals(IReadOnlyList<int>? x, IReadOnlyList<int>? y)
            => x is not null && y is not null && x.SequenceEqual(y);

        public int GetHashCode(IReadOnlyList<int> obj)
        {
            var hash = new HashCode();
            foreach (var item in obj)
            {
                hash.Add(item);
            }

            return hash.ToHashCode();
        }
    }
}
