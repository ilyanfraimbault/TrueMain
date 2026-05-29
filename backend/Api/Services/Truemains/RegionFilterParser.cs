using Core;
using Core.Lol.Identifiers;

namespace TrueMain.Services.Truemains;

/// <summary>
/// Translates the public <c>?region=</c> query param into the concrete
/// <c>PlatformId</c> strings stored on <c>riot_accounts</c>. The truemains
/// leaderboard exposes a curated set of buckets — <c>europe</c>,
/// <c>americas</c>, <c>korea</c> — and intentionally drops JP/SEA in V1
/// (the user is European; SEA gets its own bucket if/when we add it).
///
/// We deliberately do not surface every <see cref="RegionalRoute"/> value:
/// the UI filter pills are the source of truth for what's exposed, and the
/// mapping table here stays in sync with them. Membership of the underlying
/// shards in each routing region still comes from <see cref="RiotRouting.ToRegional"/>
/// so we never duplicate Riot's grouping logic.
/// </summary>
public static class RegionFilterParser
{
    /// <summary>
    /// Returns the platform ids that belong to <paramref name="region"/> (case-insensitive,
    /// whitespace-tolerant), or <c>null</c> when the slug is missing or unrecognised — meaning
    /// "no region filter, scan every platform we expose".
    /// </summary>
    public static IReadOnlyList<string>? Parse(string? region)
    {
        if (string.IsNullOrWhiteSpace(region))
        {
            return null;
        }

        var normalized = region.Trim().ToLowerInvariant();
        var routing = normalized switch
        {
            "europe" => RegionalRoute.Europe,
            "americas" => RegionalRoute.Americas,
            "korea" => RegionalRoute.Asia,
            _ => (RegionalRoute?)null,
        };

        if (routing is null)
        {
            return null;
        }

        var platforms = PlatformsInRoute(routing.Value);

        // The "korea" slug is narrower than RegionalRoute.Asia (which also
        // contains JP1). Drop JP1 here so the filter matches the UI label.
        if (normalized == "korea")
        {
            platforms = platforms.Where(p => p == nameof(PlatformRoute.KR)).ToList();
        }

        return platforms;
    }

    /// <summary>
    /// The union of all platform ids exposed by the leaderboard — used as the
    /// implicit filter when the request has no <c>?region=</c>. Mirrors the
    /// three pills in the UI: Europe + Americas + Korea (no JP/SEA in V1).
    /// </summary>
    public static IReadOnlyList<string> AllExposedPlatforms()
    {
        var europe = Parse("europe") ?? [];
        var americas = Parse("americas") ?? [];
        var korea = Parse("korea") ?? [];
        return europe.Concat(americas).Concat(korea).Distinct().ToList();
    }

    /// <summary>
    /// Maps each <see cref="RegionalRoute"/> to the slug we expose on the API.
    /// Returns <c>null</c> when the route isn't surfaced (JP1 / SEA in V1) so
    /// row mapping can skip the row instead of mislabelling it.
    /// </summary>
    public static string? RouteToSlug(string platformId)
    {
        if (!Enum.TryParse<PlatformRoute>(platformId, ignoreCase: true, out var platform))
        {
            return null;
        }

        // Korea is its own slug even though Riot groups KR+JP1 under
        // RegionalRoute.Asia — see Parse for the matching narrowing.
        if (platform == PlatformRoute.KR)
        {
            return "korea";
        }

        if (platform == PlatformRoute.JP1)
        {
            return null;
        }

        return platform.ToRegional() switch
        {
            RegionalRoute.Europe => "europe",
            RegionalRoute.Americas => "americas",
            _ => null,
        };
    }

    private static IReadOnlyList<string> PlatformsInRoute(RegionalRoute route)
        => Enum.GetValues<PlatformRoute>()
            .Where(p => p.ToRegional() == route)
            .Select(p => p.ToString())
            .ToList();
}
