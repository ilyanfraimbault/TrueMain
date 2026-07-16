using Core.Lol.Identifiers;
using Core.Lol.Map;
using Core.Lol.Patches;
using Core.Lol.Ranking;

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
    /// doesn't parse to a valid <see cref="PatchVersion"/>.
    /// </summary>
    public static string? NormalizePatch(string? raw)
        => PatchVersion.TryParse(raw, out var patch) ? patch.ToMajorMinor() : null;

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
    /// Normalises an elo-bracket filter to a canonical
    /// <see cref="EloBracket"/> constant. Returns <c>null</c> for null /
    /// whitespace / unrecognised input, which the service treats as the
    /// <c>ALL</c> (every-bracket) default.
    /// </summary>
    public static string? NormalizeEloBracket(string? raw)
        => EloBracket.Normalize(raw);
}
