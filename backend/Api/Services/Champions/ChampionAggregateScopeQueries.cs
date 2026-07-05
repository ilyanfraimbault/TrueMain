using Data.Entities;

namespace TrueMain.Services.Champions;

/// <summary>
/// LINQ helpers that share the (champion, queue, account, patch, platform, position)
/// filter between the foundation and build-tree query services. The filter must stay
/// identical across both endpoints — they have to read the same slice of aggregate
/// data, otherwise a foundation hit can pair with an empty build tree.
/// </summary>
internal static class ChampionAggregateScopeQueries
{
    public static IQueryable<ChampionAggregateScope> WhereChampionScope(
        this IQueryable<ChampionAggregateScope> source,
        int championId,
        int queueId,
        Guid? riotAccountId,
        string? patch,
        string? platformId,
        string? position,
        IReadOnlyCollection<string>? eloBrackets = null)
    {
        var query = source.Where(scope => scope.ChampionId == championId && scope.QueueId == queueId);

        if (riotAccountId.HasValue)
        {
            query = query.Where(scope => scope.RiotAccountId == riotAccountId.Value);
        }

        if (!string.IsNullOrWhiteSpace(platformId))
        {
            query = query.Where(scope => scope.PlatformId == platformId);
        }

        if (!string.IsNullOrWhiteSpace(patch))
        {
            query = query.Where(scope => scope.GameVersion == patch);
        }

        if (!string.IsNullOrWhiteSpace(position))
        {
            query = query.Where(scope => scope.Position == position);
        }

        // A null bracket set means "every game" (the ALL read-time union across
        // every persisted tier). A non-null set narrows to those tiers — one
        // tier for a "rank only" filter, several for a "rank and above" one —
        // via a single IN over the bracket-aware index.
        if (eloBrackets is { Count: > 0 })
        {
            query = query.Where(scope => eloBrackets.Contains(scope.EloBracket));
        }

        return query;
    }
}
