using Core.Lol.Patches;
using Data.Entities;

namespace TrueMain.Services.Champions;

internal static class ChampionAggregateScopeResolver
{
    public static string? ResolvePatchVersion(
        IReadOnlyCollection<ChampionAggregateScope> scopes,
        string? requestedPatch)
        => ResolvePatchVersion(
            scopes.Select(scope => scope.GameVersion).ToList(),
            requestedPatch);

    public static string? ResolvePatchVersion(
        IReadOnlyCollection<string> gameVersions,
        string? requestedPatch)
    {
        if (!string.IsNullOrWhiteSpace(requestedPatch))
        {
            // Defence in depth: the controller already canonicalises HTTP
            // query params, but service-layer callers (tests, future
            // entrypoints) might pass a full Riot version string like
            // "16.4.521.123". Aggregates persist as "major.minor", so we
            // canonicalise here too to avoid silent empty results.
            var normalized = PatchVersion.Normalize(requestedPatch);
            return string.IsNullOrEmpty(normalized) ? null : normalized;
        }

        return gameVersions
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(ParsePatchVersion)
            .FirstOrDefault();
    }

    public static string ResolveDominantPosition(IReadOnlyCollection<ChampionAggregateScope> scopes)
        => ResolveDominantPosition(scopes.Select(scope => (scope.Position, scope.Games)).ToList());

    public static string ResolveDominantPosition(IReadOnlyCollection<(string Position, int Games)> rows)
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
