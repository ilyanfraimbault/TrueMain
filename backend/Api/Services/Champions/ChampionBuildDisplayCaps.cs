namespace TrueMain.Services.Champions;

/// <summary>
/// How much of a champion's build surface is served, shared by the two paths
/// that feed the <b>same</b> Vue build panel: the aggregate read
/// (<see cref="ChampionBuildsQueryService"/>, no <c>?vs=</c> filter) and the
/// live matchup fold (<see cref="LiveBuildVariationAggregator"/>, behind a
/// <c>?vs=</c> filter).
///
/// <para>
/// They used to disagree — 4 tabs / 3 variations on the aggregate path against
/// 3 tabs / 5 variations on the matchup path, under a comment claiming they
/// matched — so picking an opponent silently removed a build tab and added two
/// variations per dimension, as if the filter had changed what a build is. The
/// caps describe the component, not the data source, so they live in one place.
/// </para>
///
/// <para>
/// These are product thresholds and belong in <c>ChampionsListOptions</c>
/// eventually (#1241, which migrates the build thresholds to named options);
/// until then, one shared pair is what keeps the two surfaces honest.
/// </para>
/// </summary>
internal static class ChampionBuildDisplayCaps
{
    /// <summary>
    /// Build tabs served for a slice, most played first. Four is the aggregate
    /// path's long-standing value and what the tab strip is laid out for.
    /// </summary>
    public const int MaxBuilds = 4;

    /// <summary>
    /// Variations listed per dimension (spells, starters, skill order, boots),
    /// most played first. Three, the aggregate path's value: a matchup slice
    /// holds 4 games at the median (see <see cref="LiveBuildVariationAggregator"/>),
    /// so a fourth and fifth row there are single games rendered as a trend.
    /// </summary>
    public const int MaxVariations = 3;
}
