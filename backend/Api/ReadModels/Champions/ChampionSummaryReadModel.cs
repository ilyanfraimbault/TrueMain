namespace TrueMain.ReadModels.Champions;

/// <summary>
/// One row of the champion directory (<c>GET /champions</c>). Computed
/// against the champion's own latest patch — there is no global patch pin
/// for the list view.
/// </summary>
public sealed class ChampionSummaryReadModel
{
    public int ChampionId { get; init; }

    public int Games { get; init; }

    public double WinRate { get; init; }

    public int TrueMainCount { get; init; }

    public string Position { get; init; } = string.Empty;

    public string LatestPatchVersion { get; init; } = string.Empty;

    public DateTime LastUpdatedAtUtc { get; init; }
}
