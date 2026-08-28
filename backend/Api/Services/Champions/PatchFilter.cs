using Core.Lol.Patches;

namespace TrueMain.Services.Champions;

/// <summary>
/// The one place that turns a caller-supplied patch string into the two forms
/// the champion reads need: the canonical <c>major.minor</c> value persisted on
/// aggregates, and the SQL prefix used to narrow <c>matches.game_version</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Match.GameVersion</c> stores the full Riot version ("16.4.521.1234"),
/// so a patch filter is necessarily a prefix match. Every champion read does it
/// the same way — <c>EF.Functions.Like(m.GameVersion, prefix)</c> against the
/// pattern this class builds — rather than mixing in
/// <c>string.StartsWith</c>: the two produce different SQL (<c>LIKE @p</c> vs
/// the provider's <c>starts_with</c>/<c>LIKE</c>-with-concat translation) for
/// the same question, and neither is index-assisted here anyway (there is no
/// index on <c>matches.game_version</c>, and Postgres only turns a
/// <c>LIKE</c> prefix into a range scan for a literal pattern under a
/// text-pattern-ops index — never for a parameter). One mechanism, chosen so
/// the generated predicate is identical across the area.
/// </para>
/// <para>
/// The prefix is built from the <em>normalised</em> patch, which
/// <see cref="Normalize"/> has already proven to be digits and dots — so it
/// carries no <c>LIKE</c> metacharacter and needs no escaping (unlike the
/// free-text search patterns, which go through
/// <c>TrueMain.Services.Ops.LikeEscaping</c>).
/// </para>
/// </remarks>
internal static class PatchFilter
{
    /// <summary>
    /// Normalises a Riot patch string (e.g. <c>16.4.521.123</c>) to the
    /// canonical <c>major.minor</c> form persisted on aggregates. Returns
    /// <see langword="null"/> for null / whitespace input or for any value that
    /// doesn't parse to a valid <see cref="PatchVersion"/> — which the callers
    /// treat as "every patch" rather than as a client error.
    /// </summary>
    public static string? Normalize(string? raw)
        => PatchVersion.TryParse(raw, out var patch) ? patch.ToMajorMinor() : null;

    /// <summary>
    /// Builds the <c>LIKE</c> pattern matching every full Riot version of an
    /// already-normalised <c>major.minor</c> patch. Returns
    /// <see langword="null"/> when there is no patch filter, which callers
    /// translate into "no clause at all".
    /// </summary>
    public static string? Prefix(string? normalizedPatch)
        => normalizedPatch is null ? null : $"{normalizedPatch}.%";

    /// <summary>
    /// <see cref="Normalize"/> then <see cref="Prefix"/>, for the callers that
    /// only need the SQL pattern.
    /// </summary>
    public static string? NormalizedPrefix(string? raw)
        => Prefix(Normalize(raw));
}
