using Core.Lol.Patches;

namespace TrueMain.Services.Champions;

/// <summary>
/// Turns a persisted <c>gameVersion</c> into the (major, minor) key champion
/// rows are ordered on, and makes the malformed-value fallback observable.
/// </summary>
/// <remarks>
/// A value that does not parse still sorts as <c>0.0</c> — the oldest patch —
/// which is indistinguishable from a genuine <c>0.0</c> row and quietly skews
/// the series it lands in. The ordering is deliberately left alone (#394): the
/// warning, not a different sort, is what turns the silent skew into a signal.
/// <para>
/// One instance per query. The same corrupt value can repeat across every row
/// of a result set and ops logs are deliberately signal-only (#444), so each
/// distinct offending value is warned about once per resolver — i.e. once per
/// query — instead of once per row.
/// </para>
/// </remarks>
internal sealed class PatchSortKeyResolver(ILogger logger, string surface, int championId)
{
    // Rows that don't parse keep sinking to the bottom of the sort, as before.
    private static readonly (int Major, int Minor) MalformedSortKey = (0, 0);

    private readonly HashSet<string> _warnedVersions = new(StringComparer.Ordinal);

    /// <summary>
    /// The sort key for <paramref name="gameVersion"/>, warning once per
    /// distinct unparseable value seen by this instance.
    /// </summary>
    public (int Major, int Minor) Resolve(string gameVersion)
    {
        if (PatchVersion.TryParse(gameVersion, out var version))
        {
            return (version.Major, version.Minor);
        }

        if (_warnedVersions.Add(gameVersion))
        {
            logger.LogWarning(
                "{Surface} malformed game_version champion_id={ChampionId} game_version={GameVersion}; row sorts as the oldest patch",
                surface, championId, gameVersion);
        }

        return MalformedSortKey;
    }
}
