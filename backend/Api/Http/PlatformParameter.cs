using System.Diagnostics.CodeAnalysis;
using Core.Lol.Identifiers;
using Microsoft.AspNetCore.Mvc;

namespace TrueMain.Http;

/// <summary>
/// The platform-route query parameter (<c>region</c> / <c>platformId</c>): canonicalisation
/// to Riot's upper-case route form, its 400 message, and the optional-filter rule the ops
/// routes share.
/// </summary>
internal static class PlatformParameter
{
    /// <summary>
    /// Client-error detail for a value that is not a known platform route. Names the offending
    /// parameter and echoes the value, so a caller passing several can tell which failed.
    /// </summary>
    public static string InvalidMessage(string parameterName, string value)
        => $"{parameterName} '{value}' is not a known platform route (e.g. EUW1, KR, NA1).";

    /// <summary>
    /// Normalises a platform identifier to the canonical Riot upper-case form (e.g.
    /// <c>EUW1</c>). Returns <c>null</c> for null / whitespace input or for any value that
    /// doesn't parse to a known platform — the alternative, passing the raw string through,
    /// would cause a silent empty result downstream.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return PlatformId.TryParse(raw, out var platformId) ? platformId.Value : null;
    }

    /// <summary>
    /// Parses an optional platform-route filter. Blank means "every region"
    /// (<paramref name="platformId"/> comes back null); an unknown route is a 400 rather than
    /// an empty page, which would read as "nothing here" instead of "that is not a region".
    /// </summary>
    public static bool TryNormalizeOptionalPlatform(
        this ControllerBase controller,
        string? region,
        string parameterName,
        out string? platformId,
        [NotNullWhen(false)] out ActionResult? problem)
    {
        if (string.IsNullOrWhiteSpace(region))
        {
            platformId = null;
            problem = null;
            return true;
        }

        if (!PlatformId.TryParse(region.Trim(), out var platform))
        {
            platformId = null;
            problem = controller.ValidationProblem(InvalidMessage(parameterName, region));
            return false;
        }

        platformId = platform.Value;
        problem = null;
        return true;
    }
}
