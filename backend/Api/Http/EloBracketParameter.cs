using System.Diagnostics.CodeAnalysis;
using Core.Lol.Ranking;
using Microsoft.AspNetCore.Mvc;

namespace TrueMain.Http;

/// <summary>
/// The <c>eloBracket</c> query parameter: canonicalisation to an <see cref="EloBracket"/>
/// constant, the 400 message, and the acceptance rule the champion routes share.
/// </summary>
internal static class EloBracketParameter
{
    /// <summary>
    /// Client-error detail returned when an <c>eloBracket</c> query parameter is present but
    /// is not a bracket.
    /// </summary>
    public const string InvalidMessage =
        "eloBracket must be ALL, a tier (IRON…CHALLENGER), or a tier with the _PLUS suffix (e.g. GOLD_PLUS).";

    /// <summary>
    /// Normalises an elo-bracket filter to a canonical <see cref="EloBracket"/> constant. A
    /// blank value means "every bracket" and yields <see langword="true"/> with a null
    /// <paramref name="normalized"/>; a non-blank value that is not a bracket yields
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// The two must not read alike, which is why this is a Try and not a normaliser returning
    /// null for both. Answering <c>?eloBracket=GOLDD</c> with the every-bracket default serves
    /// the whole population under a rank label — a fabricated number rather than a lenient
    /// filter (#1224).
    /// </remarks>
    public static bool TryNormalize(string? raw, out string? normalized)
    {
        if (!EloBracket.TryResolveFilter(raw, out _))
        {
            normalized = null;
            return false;
        }

        normalized = EloBracket.Normalize(raw);
        return true;
    }

    /// <summary>
    /// Canonicalises an optional <c>eloBracket</c>: a missing/blank value means "every
    /// bracket" (<paramref name="normalizedBracket"/> comes back null), while a non-blank
    /// value that is not a bracket is a 400 <paramref name="problem"/>.
    /// </summary>
    /// <remarks>
    /// Rejected rather than ignored, because ignoring it is not the lenient option — see
    /// <see cref="TryNormalize"/>. The same treatment the sibling <c>position</c> filter gets
    /// on these routes.
    /// </remarks>
    public static bool TryNormalizeOptionalEloBracket(
        this ControllerBase controller,
        string? eloBracket,
        out string? normalizedBracket,
        [NotNullWhen(false)] out ActionResult? problem)
    {
        if (!TryNormalize(eloBracket, out normalizedBracket))
        {
            problem = controller.ValidationProblem(InvalidMessage);
            return false;
        }

        problem = null;
        return true;
    }
}
