using Core.Lol.Patches;

namespace TrueMain.Services.Champions;

/// <summary>
/// The one place that turns a caller-supplied patch string into the canonical
/// <c>major.minor</c> value every champion read compares against.
/// </summary>
/// <remarks>
/// <para>
/// <c>Match.GameVersion</c> stores the full Riot version ("16.4.521.1234"), so a
/// patch filter used to be a prefix match — <c>EF.Functions.Like(m.GameVersion,
/// "16.4.%")</c> — and, as this file used to say at length, it was never
/// index-assisted: there is no index on <c>GameVersion</c>, and Postgres only
/// turns a <c>LIKE</c> prefix into a range scan for a literal pattern under a
/// text-pattern-ops index, never for a parameter. With
/// <c>max_parallel_workers_per_gather = 0</c> (#589) that made every champion
/// read a single-threaded scan of <c>matches</c>.
/// </para>
/// <para>
/// Since #1368 the database carries the answer: <c>matches."Patch"</c> is a
/// stored generated column holding exactly what <see cref="Normalize"/> returns
/// (see <c>Data.Configurations.MatchConfiguration.PatchComputedColumnSql</c>),
/// and it is indexed. The filter is now a plain equality —
/// <c>m.Patch == normalizedPatch</c> — and this class is down to its one
/// remaining job: normalising the caller's input to the same canonical form the
/// column holds. A <see langword="null"/> normalised patch still means "every
/// patch", i.e. no clause at all.
/// </para>
/// </remarks>
internal static class PatchFilter
{
    /// <summary>
    /// Normalises a Riot patch string (e.g. <c>16.4.521.123</c>) to the
    /// canonical <c>major.minor</c> form persisted on aggregates and on
    /// <c>matches."Patch"</c>. Returns <see langword="null"/> for null /
    /// whitespace input or for any value that doesn't parse to a valid
    /// <see cref="PatchVersion"/> — which the callers treat as "every patch"
    /// rather than as a client error.
    /// </summary>
    public static string? Normalize(string? raw)
        => PatchVersion.TryParse(raw, out var patch) ? patch.ToMajorMinor() : null;
}
