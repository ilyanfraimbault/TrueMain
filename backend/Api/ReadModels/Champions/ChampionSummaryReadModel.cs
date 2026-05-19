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
    /// Share of all games on this position taken by this champion. Standalone
    /// "Pickrate" column in the table.
    /// </summary>
    public double PickRate { get; init; }

    /// <summary>
    /// Share of this champion's own games played on this position
    /// (role distribution). Shown next to the lane icon — e.g. "MID 60%" for
    /// a champion that plays 60% of its games mid.
    /// </summary>
    public double LanePlayRate { get; init; }

    public int TrueMainCount { get; init; }

    public string Position { get; init; } = string.Empty;

    public string PatchVersion { get; init; } = string.Empty;

    public DateTime LastUpdatedAtUtc { get; init; }
}
