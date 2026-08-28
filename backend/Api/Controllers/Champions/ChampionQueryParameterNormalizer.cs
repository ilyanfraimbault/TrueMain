using Core.Lol.Identifiers;
using Core.Lol.Map;
using Core.Lol.Ranking;
using TrueMain.Services.Champions;

namespace TrueMain.Controllers.Champions;

/// <summary>
/// Canonicalises raw HTTP query parameters into the exact string forms
/// stored in <c>champion_aggregate_scopes</c>. Without this layer, callers
/// that send <c>?platformId=euw1</c>, <c>?patch=16.4.521</c> or
/// <c>?position=mid</c> get silent 404s because the WHERE clause does an
/// exact-string comparison against the canonical persisted values
/// (<c>EUW1</c>, <c>16.4</c>, <c>MIDDLE</c>).
/// </summary>
internal static class ChampionQueryParameterNormalizer
{
    /// <summary>
    /// Client-error detail returned when a <c>position</c> query parameter
    /// that must canonicalise fails to (missing where required, or
    /// unrecognised).
    /// </summary>
    public const string InvalidPositionMessage =
        "position must be one of TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY.";

    /// <summary>
    /// Normalises a Riot patch string (e.g. <c>16.4.521.123</c>) to the
    /// canonical <c>major.minor</c> form persisted on aggregates.
    /// Returns <c>null</c> for null / whitespace input or for any value that
    /// doesn't parse to a valid patch. Delegates to
    /// <see cref="PatchFilter.Normalize"/>, which the champion query services
    /// call directly — the rule lives in one place so the HTTP boundary and the
    /// reads cannot canonicalise differently.
    /// </summary>
    public static string? NormalizePatch(string? raw)
        => PatchFilter.Normalize(raw);

    /// <summary>
    /// Normalises a platform identifier to the canonical Riot upper-case
    /// form (e.g. <c>EUW1</c>). Returns <c>null</c> for null / whitespace
    /// input or for any value that doesn't parse to a known platform —
    /// the alternative (passing the raw string through) would cause a
    /// silent 404 downstream.
    /// </summary>
    public static string? NormalizePlatform(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return PlatformId.TryParse(raw, out var platformId) ? platformId.Value : null;
    }

    /// <summary>
    /// Normalises a team position to the canonical Riot upper-case form
    /// (<c>TOP</c> / <c>JUNGLE</c> / <c>MIDDLE</c> / <c>BOTTOM</c> /
    /// <c>UTILITY</c>). Returns <c>null</c> for null / whitespace input
    /// or for any value that doesn't map to a recognised position.
    /// </summary>
    public static string? NormalizePosition(string? raw)
        => LolPositionExtensions.Parse(raw).ToRiotString();

    /// <summary>
    /// Client-error detail returned when an <c>eloBracket</c> query parameter is
    /// present but is not a bracket.
    /// </summary>
    public const string InvalidEloBracketMessage =
        "eloBracket must be ALL, a tier (IRON…CHALLENGER), or a tier with the _PLUS suffix (e.g. GOLD_PLUS).";

    /// <summary>
    /// Normalises an elo-bracket filter to a canonical <see cref="EloBracket"/>
    /// constant. A blank value means "every bracket" and yields
    /// <see langword="true"/> with a null <paramref name="normalized"/>; a non-blank
    /// value that is not a bracket yields <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// The two must not read alike, which is why this is a Try and not a normaliser
    /// returning null for both. Answering <c>?eloBracket=GOLDD</c> with the
    /// every-bracket default serves the whole population under a rank label — a
    /// fabricated number rather than a lenient filter (#1224). Rejected the same way
    /// an unrecognised <c>position</c> is, so the two scope filters on the same routes
    /// behave alike.
    /// </remarks>
    public static bool TryNormalizeEloBracket(string? raw, out string? normalized)
    {
        if (!EloBracket.TryResolveFilter(raw, out _))
        {
            normalized = null;
            return false;
        }

        normalized = EloBracket.Normalize(raw);
        return true;
    }
}
