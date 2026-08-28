using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc;

namespace TrueMain.Controllers.Champions;

/// <summary>
/// The two <c>position</c> query-parameter rules the public controllers share.
/// Extension methods on <see cref="ControllerBase"/> rather than a base class:
/// the controllers have nothing else in common, and this keeps the 400 body
/// (RFC 7807 via <c>ValidationProblem</c>) produced by the controller itself.
/// </summary>
internal static class PositionParameter
{
    /// <summary>
    /// Canonicalises a required <c>position</c> query parameter; a missing or
    /// unrecognised value yields a 400 <paramref name="problem"/>. Endpoints
    /// where position is optional call
    /// <see cref="TryNormalizeOptionalPosition"/> instead.
    /// </summary>
    public static bool TryRequirePosition(
        this ControllerBase controller,
        string? position,
        [NotNullWhen(true)] out string? normalizedPosition,
        [NotNullWhen(false)] out ActionResult? problem)
    {
        normalizedPosition = ChampionQueryParameterNormalizer.NormalizePosition(position);
        if (normalizedPosition is null)
        {
            problem = controller.ValidationProblem(ChampionQueryParameterNormalizer.InvalidPositionMessage);
            return false;
        }

        problem = null;
        return true;
    }

    /// <summary>
    /// Canonicalises an optional <c>position</c> query parameter: a
    /// missing/blank value means "all positions" (<paramref name="normalizedPosition"/>
    /// comes back null), while a non-blank value that fails to canonicalise is
    /// a 400 <paramref name="problem"/> rather than silently falling back to
    /// "no filter".
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
