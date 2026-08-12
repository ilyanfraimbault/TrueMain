namespace TrueMain.ReadModels.Champions;

/// <summary>
/// How much a single patch actually holds (#1109) — the counters both the serving
/// decision and the homepage chips read, without computing the directory itself.
///
/// <para>
/// Deliberately cheaper than <see cref="ChampionSummariesResult"/>: it takes one
/// grouped scan and skips top builds, ban rates, the dominant-lane filter and the
/// tier percentiles. The homepage sums these across two patches, and paying for two
/// full directories to print two numbers would double the cost of the most-hit page
/// on the site.
/// </para>
/// </summary>
public sealed record ChampionPatchVolume
{
    /// <summary>The patch as stored on the aggregate rows.</summary>
    public string Patch { get; init; } = string.Empty;

    /// <summary>
    /// Every aggregated game on the patch, lane-less scopes and below-floor lines
    /// included — the same definition as <see cref="ChampionSummariesResult.TotalGames"/>,
    /// so a one-patch window prints the identical number it printed before #1109.
    /// </summary>
    public long TotalGames { get; init; }

    /// <summary>
    /// <c>(champion, lane)</c> lines clearing <c>ChampionsList:MinSampleGames</c> —
    /// what the directory would render for this patch, and what the servable bar is
    /// measured against.
    /// </summary>
    public int LinesPastFloor { get; init; }

    /// <summary>
    /// Distinct champions holding at least one line past the floor. Equal to the
    /// directory's <see cref="ChampionSummariesResult.ChampionsRanked"/> for the same
    /// patch: the dominant-lane cap drops a champion's <em>extra</em> lines but never
    /// its most-played one, so no champion is lost between the two counts.
    /// </summary>
    public IReadOnlyList<int> ChampionsPastFloor { get; init; } = [];
}
