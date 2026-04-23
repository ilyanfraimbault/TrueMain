using Data.Entities;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

internal static class ChampionRunePageAggregator
{
    /// <summary>
    /// Top-3 rune pages across all first items (overall champion-level view).
    /// Collapses the <see cref="ChampionAggregateRunePage.FirstItemId"/>
    /// dimension so the same page played with different first items still
    /// counts as one option (what users expect on the champion summary).
    /// </summary>
    public static IReadOnlyList<RunePageOptionReadModel> AggregateTopThree(
        IReadOnlyCollection<ChampionAggregateRunePage> rows,
        int sampleSize)
        => GroupByPage(rows, includeFirstItemInKey: false)
            .Select(group => Project(firstItemId: 0, group, sampleSize))
            .OrderByDescending(option => option.Games)
            .ThenByDescending(option => option.WinRate)
            .ThenBy(option => option.PrimaryStyleId)
            .ThenBy(option => option.PrimaryKeystoneId)
            .Take(3)
            .ToList();

    /// <summary>
    /// Top rune page correlated with a specific first completed build item —
    /// the "when rushing X, play rune page Y" view used by the build-tree
    /// branches. Returns null when no rune page is recorded for that first
    /// item (e.g. backfilled rows where FirstItemId is still 0). The play
    /// rate is scoped to games where this first item was picked, not to the
    /// whole sample, so it always sums to 1 across rune pages for that item.
    /// </summary>
    public static RunePageOptionReadModel? SelectTopForFirstItem(
        IReadOnlyCollection<ChampionAggregateRunePage> rows,
        int firstItemId)
    {
        if (firstItemId <= 0)
        {
            return null;
        }

        var branchRows = rows.Where(row => row.FirstItemId == firstItemId).ToList();
        if (branchRows.Count == 0)
        {
            return null;
        }

        var branchSample = branchRows.Sum(row => row.Games);
        return GroupByPage(branchRows, includeFirstItemInKey: false)
            .Select(group => Project(firstItemId, group, branchSample))
            .OrderByDescending(option => option.Games)
            .ThenByDescending(option => option.WinRate)
            .ThenBy(option => option.PrimaryStyleId)
            .ThenBy(option => option.PrimaryKeystoneId)
            .FirstOrDefault();
    }

    private static IEnumerable<IGrouping<PageKey, ChampionAggregateRunePage>> GroupByPage(
        IEnumerable<ChampionAggregateRunePage> rows,
        bool includeFirstItemInKey)
        => rows.GroupBy(row => new PageKey(
            includeFirstItemInKey ? row.FirstItemId : 0,
            row.PrimaryStyleId,
            row.PrimaryKeystoneId,
            row.PrimaryPerk1Id,
            row.PrimaryPerk2Id,
            row.PrimaryPerk3Id,
            row.SecondaryStyleId,
            row.SecondaryPerk1Id,
            row.SecondaryPerk2Id,
            row.StatOffense,
            row.StatFlex,
            row.StatDefense));

    private static RunePageOptionReadModel Project(
        int firstItemId,
        IGrouping<PageKey, ChampionAggregateRunePage> group,
        int sampleSize)
    {
        var games = group.Sum(row => row.Games);
        return new RunePageOptionReadModel
        {
            FirstItemId = firstItemId,
            PrimaryStyleId = group.Key.PrimaryStyleId,
            PrimaryKeystoneId = group.Key.PrimaryKeystoneId,
            PrimaryPerk1Id = group.Key.PrimaryPerk1Id,
            PrimaryPerk2Id = group.Key.PrimaryPerk2Id,
            PrimaryPerk3Id = group.Key.PrimaryPerk3Id,
            SecondaryStyleId = group.Key.SecondaryStyleId,
            SecondaryPerk1Id = group.Key.SecondaryPerk1Id,
            SecondaryPerk2Id = group.Key.SecondaryPerk2Id,
            StatOffense = group.Key.StatOffense,
            StatFlex = group.Key.StatFlex,
            StatDefense = group.Key.StatDefense,
            Games = games,
            PlayRate = ChampionOptionProjector.ComputeRate(games, sampleSize),
            WinRate = ChampionOptionProjector.ComputeRate(group.Sum(row => row.Wins), games)
        };
    }

    private readonly record struct PageKey(
        int FirstItemId,
        int PrimaryStyleId,
        int PrimaryKeystoneId,
        int PrimaryPerk1Id,
        int PrimaryPerk2Id,
        int PrimaryPerk3Id,
        int SecondaryStyleId,
        int SecondaryPerk1Id,
        int SecondaryPerk2Id,
        int StatOffense,
        int StatFlex,
        int StatDefense);
}
