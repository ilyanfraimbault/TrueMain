using Core.Lol.Patches;
using Data.Entities;

namespace TrueMain.Services.Champions;

internal static class ChampionAggregateScopeResolver
{
    public static string? ResolvePatchVersion(
        IEnumerable<ChampionAggregateScope> scopes,
        string? requestedPatch)
    {
        if (!string.IsNullOrWhiteSpace(requestedPatch))
        {
            return NormalizeRequestedPatch(requestedPatch);
        }

        return ResolvePatchVersion(scopes.Select(scope => scope.GameVersion), requestedPatch);
    }

    public static string? ResolvePatchVersion(
        IEnumerable<string> gameVersions,
        string? requestedPatch)
    {
        if (!string.IsNullOrWhiteSpace(requestedPatch))
        {
            return NormalizeRequestedPatch(requestedPatch);
        }

        return gameVersions
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(ParsePatchVersion)
            .FirstOrDefault();
    }

    /// <summary>
    /// Picks the most recent patch whose dominant-position game count clears
    /// <paramref name="minGames"/>. For player-scoped views: a player's latest
    /// patch is often too thin to rank, so resolving to the newest patch where
    /// they actually have enough games beats defaulting to the global latest
    /// (which would render an empty "not enough games" state). Returns null when
    /// no patch clears the floor.
    /// </summary>
    public static string? ResolveLatestPatchAboveFloor(
        IEnumerable<(string GameVersion, string Position, int Games)> rows,
        int minGames)
        => rows
            .GroupBy(row => row.GameVersion, StringComparer.Ordinal)
            .Where(patchRows => patchRows
                .Where(row => !string.IsNullOrWhiteSpace(row.Position))
                .GroupBy(row => row.Position)
                .Select(positionRows => positionRows.Sum(row => row.Games))
                .DefaultIfEmpty(0)
                .Max() >= minGames)
            .Select(patchRows => patchRows.Key)
            .OrderByDescending(ParsePatchVersion)
            .FirstOrDefault();

    public static string ResolveDominantPosition(IEnumerable<ChampionAggregateScope> scopes)
        => ResolveDominantPosition(scopes.Select(scope => (scope.Position, scope.Games)));

    public static string ResolveDominantPosition(IEnumerable<(string Position, int Games)> rows)
        => rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Position))
            .GroupBy(row => row.Position)
            .Select(group => new
            {
                Position = group.Key,
                Games = group.Sum(row => row.Games)
            })
            .OrderByDescending(group => group.Games)
            .ThenBy(group => group.Position, StringComparer.Ordinal)
            .Select(group => group.Position)
            .FirstOrDefault() ?? string.Empty;

    private static string? NormalizeRequestedPatch(string requestedPatch)
    {
        // Service-layer callers may pass a full Riot version string like
        // "16.4.521.123" while aggregates persist as "major.minor". Canonicalise
        // here so SQL filters built from this value hit the persisted form.
        var normalized = PatchVersion.Normalize(requestedPatch);
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static (int Major, int Minor) ParsePatchVersion(string gameVersion)
    {
        if (string.IsNullOrWhiteSpace(gameVersion))
        {
            return (0, 0);
        }

        var segments = gameVersion.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var major = segments.Length > 0 && int.TryParse(segments[0], out var parsedMajor) ? parsedMajor : 0;
        var minor = segments.Length > 1 && int.TryParse(segments[1], out var parsedMinor) ? parsedMinor : 0;
        return (major, minor);
    }
}
