namespace TrueMain.ReadModels.Champions;

/// <summary>
/// One row of the champion directory (<c>GET /champions</c>) — one entry per
/// <c>(champion, position)</c> pair, computed against a single patch (the
/// requested one, or the global latest if unspecified). A champion played in
/// multiple lanes therefore surfaces once per lane.
/// </summary>
public sealed class ChampionSummaryReadModel
{
    public int ChampionId { get; init; }

    public int Games { get; init; }

    public int Wins { get; init; }

    public double WinRate { get; init; }

    /// <summary>
    /// Share of all games on this position taken by this champion — meta-wide
    /// pickrate, computed against every observed game on the patch (not just
    /// the TrueMain-scoped slice).
    /// </summary>
    public double PickRate { get; init; }

    /// <summary>
    /// Share of this champion's own games played on this position — i.e. the
    /// champion's role distribution. A champion played 60% mid / 40% top has
    /// <c>LanePlayRate = 0.6</c> on its mid row and <c>0.4</c> on its top row.
    /// </summary>
    public double LanePlayRate { get; init; }

    public int TrueMainCount { get; init; }

    public string Position { get; init; } = string.Empty;

    public string PatchVersion { get; init; } = string.Empty;

    public DateTime LastUpdatedAtUtc { get; init; }

    /// <summary>
    /// Most-played build for this <c>(champion, position)</c> on the active
    /// patch — keystone, secondary tree, item sequence. Null when no
    /// pattern rows exist (rare; e.g. positions with summary games but no
    /// observed builds yet).
    /// </summary>
    public TopBuildReadModel? TopBuild { get; init; }
}
