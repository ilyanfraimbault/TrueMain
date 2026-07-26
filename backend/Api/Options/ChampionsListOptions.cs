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
    /// the ranking). Ten is low enough to surface a modestly-played champion at
    /// the current data volume while still keeping single-game flukes out of the
    /// percentiles. Set to 0 to disable.
    /// </summary>
    public int MinSampleGames { get; set; } = 10;

    /// <summary>
    /// Minimum games a champion-vs-opponent lane matchup needs before the
    /// matchup endpoints include it. A handful of games against a specific
    /// opponent is noise — a single lucky game would read as a 100% matchup —
    /// so opponents below this floor are dropped from the list in SQL (a HAVING
    /// on the grouped game count); the endpoint still returns 200 with the
    /// qualifying entries (an empty list when none clear the floor). Ten is the
    /// smallest sample where the head-to-head win rate starts to carry signal
    /// rather than echoing one or two games. Set to 0 to disable the floor.
    /// </summary>
    public int MinMatchupGames { get; set; } = 10;

    /// <summary>
    /// Minimum games a champion-vs-opponent matchup needs in a
    /// <em>player-scoped</em> slice (one truemain's games) before the leaderboard
    /// includes it. A single player rarely meets the same lane opponent ten
    /// times, so reusing the global <see cref="MinMatchupGames"/> floor would
    /// empty almost every player's matchups list; this lower floor keeps the
    /// best/worst ranking meaningful without erasing it. The opponent search
    /// ignores both floors — a deliberate lookup shows the head-to-head from a
    /// single game up. Set to 0 to disable.
    /// </summary>
    public int MinPlayerMatchupGames { get; set; } = 3;

    /// <summary>
    /// Minimum games each side of the account-vs-mains comparison (#528) needs
    /// before the head-to-head is flagged comparable. Both columns are
    /// player-scoped slices of one champion, so the global
    /// <see cref="MinSampleGames"/> floor would empty almost every comparison;
    /// this sits just above the per-player matchup floor, where a win rate and a
    /// CS/min average stop echoing one or two games. The endpoint still returns
    /// both columns below the floor (with a status saying so) so the caller can
    /// show how far the sample is from the bar. Set to 0 to disable — a side
    /// with no games at all is never comparable regardless.
    /// </summary>
    public int MinComparisonGames { get; set; } = 5;
}
