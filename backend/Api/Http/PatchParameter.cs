using TrueMain.Services.Champions.Scopes;

namespace TrueMain.Http;

/// <summary>
/// The <c>patch</c> query parameter, canonicalised into the exact string form stored in
/// <c>champion_aggregate_scopes</c>. Without this step a caller sending
/// <c>?patch=16.4.521</c> gets a silent empty result, because the WHERE clause compares
/// against the canonical persisted value (<c>16.4</c>) as an exact string.
/// </summary>
internal static class PatchParameter
{
    /// <summary>
    /// Normalises a Riot patch string (e.g. <c>16.4.521.123</c>) to the canonical
    /// <c>major.minor</c> form persisted on aggregates. Returns <c>null</c> for null /
    /// whitespace input and for any value that doesn't parse to a valid patch — both mean
    /// "no patch filter", which is this parameter's documented default.
    /// </summary>
    /// <remarks>
    /// Delegates to <see cref="PatchFilter.Normalize"/>, which the champion query services
    /// call directly: the rule lives in one place so the HTTP boundary and the reads cannot
    /// canonicalise differently.
    /// </remarks>
    public static string? Normalize(string? raw)
        => PatchFilter.Normalize(raw);
}
