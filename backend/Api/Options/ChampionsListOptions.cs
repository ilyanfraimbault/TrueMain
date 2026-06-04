namespace TrueMain.Options;

/// <summary>
/// Quality thresholds for the <c>GET /champions</c> directory. Product knobs the
/// user wants to tweak without a redeploy, so they bind from
/// <c>ChampionsList:*</c> in configuration.
/// </summary>
public sealed class ChampionsListOptions
{
    public const string SectionName = "ChampionsList";

    /// <summary>
    /// Minimum games a <c>(champion, lane)</c> line needs before it earns a tier
    /// and a spot in the list. Without a floor, one-game off-role picks (a single
    /// 1-0 = 100% win rate) top the patch-wide tier percentiles as a fluke and
    /// crowd every genuinely-played champion into the middle, collapsing the whole
    /// S→D scale to A/B. Lines below this are dropped from the payload (and from
    /// the ranking). Set to 0 to disable.
    /// </summary>
    public int MinSampleGames { get; set; } = 20;

    /// <summary>
    /// Minimum games a champion-vs-opponent lane matchup needs before the
    /// matchup endpoints return a win rate. A handful of games against a
    /// specific opponent is noise — a single lucky game would read as a 100%
    /// matchup — so below this floor the slice is reported as "not enough data"
    /// (the service yields <see langword="null"/> → the controller returns
    /// 404). Ten is the smallest sample where the head-to-head win rate starts
    /// to carry signal rather than echoing one or two games. Set to 0 to
    /// disable the floor.
    /// </summary>
    public int MinMatchupGames { get; set; } = 10;
}
