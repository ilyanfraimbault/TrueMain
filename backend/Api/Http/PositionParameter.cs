using System.Diagnostics.CodeAnalysis;
using Core.Lol.Map;
using Microsoft.AspNetCore.Mvc;

namespace TrueMain.Http;

/// <summary>
/// The <c>position</c> query parameter: canonicalisation to Riot's team-position vocabulary,
/// the 400 message, and the two acceptance rules the controllers share.
/// </summary>
/// <remarks>
/// The two Try methods are extensions on <see cref="ControllerBase"/> rather than a shared
/// base class: the controllers that take a position have nothing else in common, and this
/// keeps the 400 body (RFC 7807 via <c>ValidationProblem</c>) produced by the controller
/// itself.
/// </remarks>
internal static class PositionParameter
{
    /// <summary>
    /// Client-error detail returned when a <c>position</c> query parameter that must
    /// canonicalise fails to — missing where required, or unrecognised.
    /// </summary>
    public const string InvalidMessage =
        "position must be one of TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY.";

    /// <summary>
    /// Normalises a team position to the canonical Riot upper-case form (<c>TOP</c> /
    /// <c>JUNGLE</c> / <c>MIDDLE</c> / <c>BOTTOM</c> / <c>UTILITY</c>). Returns <c>null</c>
    /// for null / whitespace input or for any value that doesn't map to a recognised
    /// position — the caller decides which of the two is an error.
    /// </summary>
    public static string? Normalize(string? raw)
        => LolPositionExtensions.Parse(raw).ToRiotString();

    /// <summary>
    /// Canonicalises a required <c>position</c>; a missing or unrecognised value yields a
    /// 400 <paramref name="problem"/>. Endpoints where position is optional call
    /// <see cref="TryNormalizeOptionalPosition"/> instead.
    /// </summary>
    public static bool TryRequirePosition(
        this ControllerBase controller,
        string? position,
        [NotNullWhen(true)] out string? normalizedPosition,
        [NotNullWhen(false)] out ActionResult? problem)
    {
        normalizedPosition = Normalize(position);
        if (normalizedPosition is null)
        {
            problem = controller.ValidationProblem(InvalidMessage);
            return false;
        }

        problem = null;
        return true;
    }

    /// <summary>
    /// Canonicalises an optional <c>position</c>: a missing/blank value means "all positions"
    /// (<paramref name="normalizedPosition"/> comes back null), while a non-blank value that
    /// fails to canonicalise is a 400 <paramref name="problem"/> rather than a silent
    /// fallback to "no filter".
    /// </summary>
    public static bool TryNormalizeOptionalPosition(
        this ControllerBase controller,
        string? position,
        out string? normalizedPosition,
        [NotNullWhen(false)] out ActionResult? problem)
    {
        if (string.IsNullOrWhiteSpace(position))
        {
            normalizedPosition = null;
            problem = null;
            return true;
        }

        return controller.TryRequirePosition(position, out normalizedPosition, out problem);
    }
}
