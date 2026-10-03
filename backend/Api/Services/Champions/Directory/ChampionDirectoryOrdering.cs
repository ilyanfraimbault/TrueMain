using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions.Directory;

/// <summary>The columns the champion directory can be ordered by (#1734).</summary>
public enum ChampionDirectorySort
{
    PickRate = 0,
    WinRate,
    BanRate,
    Games,
    Tier,
}

/// <summary>
/// The champion directory's order (#1734), pure so it can be pinned without a database.
/// Descending always means "strongest first" — highest rate or count, best tier — and
/// ascending reverses that, except for the rows that have no value to compare: a ban
/// rate the patch never observed (#920) and a tier letter nothing stamped stay last in
/// both directions, because "not measured" is not the bottom of the scale.
/// Every order ends on the same tie-breakers (pick rate, games, champion, lane) so a line
/// never moves between two pages from one request to the next.
/// </summary>
public static class ChampionDirectoryOrdering
{
    public static bool TryParseSort(string? value, out ChampionDirectorySort sort)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "pickrate":
                sort = ChampionDirectorySort.PickRate;
                return true;
            case "winrate":
                sort = ChampionDirectorySort.WinRate;
                return true;
            case "banrate":
                sort = ChampionDirectorySort.BanRate;
                return true;
            case "games":
                sort = ChampionDirectorySort.Games;
                return true;
            case "tier":
                sort = ChampionDirectorySort.Tier;
                return true;
            default:
                sort = ChampionDirectorySort.PickRate;
                return false;
        }
    }

    /// <summary><c>asc</c> reads ascending; anything else, omitted included, is the default descending order.</summary>
    public static bool IsDescending(string? order)
        => !string.Equals(order?.Trim(), "asc", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<ChampionSummaryReadModel> Apply(
        IEnumerable<ChampionSummaryReadModel> rows, ChampionDirectorySort sort, bool descending)
    {
        var ordered = sort switch
        {
            ChampionDirectorySort.WinRate => rows.By(row => row.WinRate, descending),
            ChampionDirectorySort.Games => rows.By(row => row.Games, descending),
            ChampionDirectorySort.BanRate => rows
                .OrderBy(row => row.BanRate is null)
                .Then(row => row.BanRate ?? 0, descending),
            // Best tier is the lowest rank (S = 0), so "strongest first" is rank ascending,
            // then the blended score that placed the line within its letter.
            ChampionDirectorySort.Tier => rows
                .OrderBy(row => ChampionTierCalculator.TierRank(row.Tier) == ChampionTierCalculator.TierOrder.Length)
                .Then(row => ChampionTierCalculator.TierRank(row.Tier), !descending)
                .Then(row => row.TierScore, descending),
            _ => rows.By(row => row.PickRate, descending),
        };

        return ordered
            .ThenByDescending(row => row.PickRate)
            .ThenByDescending(row => row.Games)
            .ThenBy(row => row.ChampionId)
            .ThenBy(row => row.Position, StringComparer.Ordinal)
            .ToList();
    }

    private static IOrderedEnumerable<T> By<T, TKey>(
        this IEnumerable<T> source, Func<T, TKey> key, bool descending)
        => descending ? source.OrderByDescending(key) : source.OrderBy(key);

    private static IOrderedEnumerable<T> Then<T, TKey>(
        this IOrderedEnumerable<T> source, Func<T, TKey> key, bool descending)
        => descending ? source.ThenByDescending(key) : source.ThenBy(key);
}
