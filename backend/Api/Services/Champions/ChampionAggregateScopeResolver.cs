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
    /// Resolves the patch immediately preceding <paramref name="activePatch"/>:
    /// the highest <c>GameVersion</c> in <paramref name="gameVersions"/> that is
    /// strictly below it. Used to back the win-rate delta on the champion list.
    /// Returns <c>null</c> when <paramref name="activePatch"/> is unparseable or
    /// no earlier patch exists in the set.
    /// </summary>
    public static string? ResolvePreviousPatchVersion(
        IEnumerable<string> gameVersions,
        string activePatch)
    {
        if (!PatchVersion.TryParse(activePatch, out var active))
        {
            return null;
        }

        return gameVersions
            .Distinct(StringComparer.Ordinal)
            .Select(version => (Version: version, Parsed: ParsePatchVersion(version)))
            .Where(candidate => candidate.Parsed.Major != 0 || candidate.Parsed.Minor != 0)
            .Where(candidate => new PatchVersion(candidate.Parsed.Major, candidate.Parsed.Minor) < active)
            .OrderByDescending(candidate => candidate.Parsed)
            .Select(candidate => candidate.Version)
            .FirstOrDefault();
    }

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
