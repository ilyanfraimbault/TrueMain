namespace Core.Lol.Map;

/// <summary>
/// The five Summoner's Rift lane positions in Riot's <c>teamPosition</c> vocabulary — the
/// single representation of a role across the solution. The schema stores
/// <c>TeamPosition</c> as this string, so there is no enum beside it: read filters, fold
/// cohorts and data-quality rules all test membership in <see cref="All"/>.
/// </summary>
public static class LanePositions
{
    /// <summary>The five canonical lane positions, in canonical order.</summary>
    public static readonly IReadOnlyList<string> All = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];

    /// <summary>
    /// Whether <paramref name="teamPosition"/> is exactly one of <see cref="All"/> — the
    /// stored-data test: no trimming, no case folding, no alias.
    /// </summary>
    public static bool IsLane(string? teamPosition)
        => teamPosition is not null && All.Contains(teamPosition, StringComparer.Ordinal);

    /// <summary>
    /// Canonicalises a position typed by a client (query parameter, request body) to one of
    /// <see cref="All"/>: trimmed, upper-cased, with the common short forms <c>MID</c> and
    /// <c>BOT</c> accepted for <c>MIDDLE</c> and <c>BOTTOM</c>. Returns <c>null</c> for a
    /// null, blank or unrecognised value — the caller decides whether that is an error or
    /// "no filter".
    /// </summary>
    public static string? Normalize(string? raw)
    {
        var upper = raw?.Trim().ToUpperInvariant();
        return upper switch
        {
            "MID" => "MIDDLE",
            "BOT" => "BOTTOM",
            _ => IsLane(upper) ? upper : null,
        };
    }
}
