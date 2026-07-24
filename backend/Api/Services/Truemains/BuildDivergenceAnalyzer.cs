namespace TrueMain.Services.Truemains;

/// <summary>
/// One aggregate pattern row reduced to the columns the "you vs mains"
/// comparison needs. Starter set and skill order are compared by dimension id
/// rather than by value: <c>champion_dim_starter_items</c> and
/// <c>champion_dim_skill_orders</c> are globally deduplicated on their key
/// (UNIQUE index), so equal ids mean equal keys across any two pools — no join
/// is needed to tell whether two players made the same choice.
///
/// <c>StarterItemsId</c> / <c>SkillOrderId</c> are the FKs to those two
/// deduplicated dimensions; <c>BootsItemId</c> is the completed boots (0 when
/// the game had none); <c>BuildItem0..6</c> are the completed non-boots items
/// in completion order; <c>Games</c> / <c>Wins</c> are the counts behind the row.
/// </summary>
internal sealed record DivergencePatternRow(
    Guid StarterItemsId,
    Guid SkillOrderId,
    int BootsItemId,
    int BuildItem0,
    int BuildItem1,
    int BuildItem2,
    int BuildItem3,
    int BuildItem4,
    int BuildItem5,
    int BuildItem6,
    int Games,
    int Wins)
{
    /// <summary>
    /// Completed build item at <paramref name="index"/> in completion order,
    /// or 0 past the end of the build. Boots are never in here — the
    /// aggregation pipeline files them under <see cref="BootsItemId"/>.
    /// </summary>
    public int ItemAt(int index) => index switch
    {
        0 => BuildItem0,
        1 => BuildItem1,
        2 => BuildItem2,
        3 => BuildItem3,
        4 => BuildItem4,
        5 => BuildItem5,
        6 => BuildItem6,
        _ => 0
    };
}

/// <summary>A pool's dominant choice on one keyed dimension.</summary>
internal readonly record struct KeyedChoice<TKey>(TKey Key, int Games, int Wins);

/// <summary>
/// A pool's dominant completed-item progression, with the games / wins of the
/// slice that actually followed it end to end.
/// </summary>
internal readonly record struct PathChoice(IReadOnlyList<int> ItemIds, int Games, int Wins);

/// <summary>
/// Pure comparison logic behind the "you vs mains" card. Takes two already
/// materialised pools of aggregate pattern rows (the player's, and every other
/// main's for the same champion + patch + position) and reduces each to its
/// dominant choice per dimension, plus the cross-pool counts needed to say how
/// common one pool's choice is in the other.
///
/// Kept free of EF and of the read models so the ranking / tie-breaking / path
/// walking is unit-testable without a database.
/// </summary>
internal static class BuildDivergenceAnalyzer
{
    /// <summary>
    /// How many completed items the compared build path is capped at. Three is
    /// where a build stops being a decision and starts being a reaction to the
    /// game — and it keeps the compared sequence short enough that a mismatch
    /// points at something specific instead of "your whole build differs".
    /// </summary>
    public const int CoreItemDepth = 3;

    /// <summary>
    /// A step is only appended to the path when at least this share of the
    /// games that reached the previous step took it. Below that the pool has no
    /// dominant next item and the path honestly stops short rather than
    /// crowning a plurality of one. Mirrors
    /// <c>ChampionBuildPathAnalyzer.ItemPathProbThreshold</c>, which gates the
    /// build path shown on the champion page the same way.
    /// </summary>
    public const double MinPathStepRate = 0.20;

    /// <summary>
    /// The pool's most-played value of <paramref name="keySelector"/>, or
    /// <see langword="null"/> when no row qualifies. Ties break by wins then by
    /// the key itself so the same pool always yields the same answer.
    /// </summary>
    public static KeyedChoice<TKey>? TopChoice<TKey>(
        IEnumerable<DivergencePatternRow> rows,
        Func<DivergencePatternRow, TKey> keySelector)
        where TKey : IComparable<TKey>
    {
        var ranked = rows
            .GroupBy(keySelector)
            .Select(group => new KeyedChoice<TKey>(
                group.Key,
                group.Sum(row => row.Games),
                group.Sum(row => row.Wins)))
            .Where(choice => choice.Games > 0)
            .OrderByDescending(choice => choice.Games)
            .ThenByDescending(choice => choice.Wins)
            .ThenBy(choice => choice.Key)
            .ToList();

        return ranked.Count == 0 ? null : ranked[0];
    }

    /// <summary>
    /// Games / wins the pool posted on one specific key — the cross-pool
    /// lookup behind "only 4% of mains do what you do".
    /// </summary>
    public static (int Games, int Wins) TotalsForKey<TKey>(
        IEnumerable<DivergencePatternRow> rows,
        Func<DivergencePatternRow, TKey> keySelector,
        TKey key)
        where TKey : IEquatable<TKey>
    {
        var games = 0;
        var wins = 0;
        foreach (var row in rows)
        {
            if (!keySelector(row).Equals(key))
            {
                continue;
            }
            games += row.Games;
            wins += row.Wins;
        }
        return (games, wins);
    }

    /// <summary>
    /// Greedy walk of the pool's dominant completed-item progression: at each
    /// depth, keep the most-played next item among the games that already
    /// followed the path, stop at <see cref="CoreItemDepth"/> or as soon as no
    /// next item clears <see cref="MinPathStepRate"/>.
    ///
    /// Rows with no completed item at all are excluded up front (a game that
    /// ended before the first item tells us nothing about build preference),
    /// but a row that merely stops <em>short</em> of the walked depth still
    /// counts in the denominator — otherwise a path that only 20% of games
    /// reach would report as near-unanimous.
    /// </summary>
    public static PathChoice WalkCorePath(IReadOnlyList<DivergencePatternRow> rows)
    {
        var current = rows.Where(row => row.BuildItem0 > 0).ToList();
        var games = current.Sum(row => row.Games);
        var wins = current.Sum(row => row.Wins);
        var path = new List<int>();

        for (var depth = 0; depth < CoreItemDepth; depth++)
        {
            var parentGames = games;
            if (parentGames == 0)
            {
                break;
            }

            var best = TopChoice(current.Where(row => row.ItemAt(depth) > 0), row => row.ItemAt(depth));
            if (best is null || (double)best.Value.Games / parentGames < MinPathStepRate)
            {
                break;
            }

            path.Add(best.Value.Key);
            current = current.Where(row => row.ItemAt(depth) == best.Value.Key).ToList();
            games = best.Value.Games;
            wins = best.Value.Wins;
        }

        return new PathChoice(path, path.Count == 0 ? 0 : games, path.Count == 0 ? 0 : wins);
    }

    /// <summary>
    /// Games / wins the pool posted on builds whose completed items start with
    /// <paramref name="path"/>. An empty path matches nothing — there is no
    /// choice to count.
    /// </summary>
    public static (int Games, int Wins) TotalsForPath(
        IEnumerable<DivergencePatternRow> rows,
        IReadOnlyList<int> path)
    {
        if (path.Count == 0)
        {
            return (0, 0);
        }

        var games = 0;
        var wins = 0;
        foreach (var row in rows)
        {
            var matches = true;
            for (var depth = 0; depth < path.Count; depth++)
            {
                if (row.ItemAt(depth) == path[depth])
                {
                    continue;
                }
                matches = false;
                break;
            }

            if (!matches)
            {
                continue;
            }

            games += row.Games;
            wins += row.Wins;
        }

        return (games, wins);
    }
}
