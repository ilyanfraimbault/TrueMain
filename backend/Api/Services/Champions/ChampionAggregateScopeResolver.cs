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
    /// Deduplicated and newest-first — the order every default-patch walk uses, and
    /// the one place the "which patch is newer" comparison lives.
    /// </summary>
    public static IReadOnlyList<string> OrderNewestFirst(IEnumerable<string> gameVersions)
        => [.. gameVersions.Distinct(StringComparer.Ordinal).OrderByDescending(ParsePatchVersion)];

    /// <summary>
    /// Picks the patch the patch-less public reads default to (#1109): walking back
    /// from the newest, the first one carrying at least <paramref name="minLines"/>
    /// <c>(champion, lane)</c> lines above the directory's min-sample floor.
    ///
    /// <para>
    /// The plain "newest patch with any row at all" rule this replaced put the site
    /// on a patch its own directory then filtered down to nothing, for the whole
    /// window between a patch's first fold and its first few thousand games. A patch
    /// becomes current here only once it can fill the page it is about to be shown on.
    /// </para>
    ///
    /// <para>
    /// Falls back to the newest patch when <em>nothing</em> clears the bar rather
    /// than returning null: on a fresh deployment, or with a bar set above the whole
    /// site's volume, a thin directory is the honest state and an empty one is not.
    /// The same fallback makes a zero or negative <paramref name="minLines"/> the
    /// documented off-switch.
    /// </para>
    /// </summary>
    /// <param name="gameVersions">Candidate patches, in any order.</param>
    /// <param name="linesPastFloorByPatch">
    /// Lines clearing the floor, per patch — keyed by the same strings
    /// <paramref name="gameVersions"/> carries. A patch absent from the lookup counts
    /// as zero, so a candidate whose lines were never measured can never win.
    /// </param>
    /// <param name="minLines">The bar, <c>ChampionsList:MinServablePatchLines</c>.</param>
    public static string? ResolveServablePatch(
        IEnumerable<string> gameVersions,
        IReadOnlyDictionary<string, int> linesPastFloorByPatch,
        int minLines)
    {
        var ordered = OrderNewestFirst(gameVersions);
        if (minLines <= 0)
        {
            return ordered.FirstOrDefault();
        }

        foreach (var gameVersion in ordered)
        {
            if (linesPastFloorByPatch.GetValueOrDefault(gameVersion) >= minLines)
            {
                return gameVersion;
            }
        }

        return ordered.FirstOrDefault();
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
