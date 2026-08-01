namespace TrueMain.ReadModels.Champions;

/// <summary>
/// Homepage-sized snapshot of the champion directory (<c>GET /champions/overview</c>,
/// #972): the true "games analyzed this patch" total plus a short, pre-sorted
/// slice of the strongest rows, so the homepage never has to fetch and sort
/// the full ~500-row directory just to render a stat chip and an 8-row teaser.
/// Always scoped to the active patch, no elo bracket filter — the homepage has
/// none of its own.
/// </summary>
public sealed record ChampionOverviewReadModel
{
    /// <summary>Canonical <c>major.minor</c> patch this snapshot was computed for.</summary>
    public string PatchVersion { get; init; } = string.Empty;

    /// <summary>
    /// True sum of games aggregated across every <c>(champion, position)</c>
    /// slice on the active patch — see <see cref="ChampionSummariesResult.TotalGames"/>,
    /// which this is copied from. Not the sum of <see cref="TopRows"/> (a short slice)
    /// nor of the ranked directory alone (which excludes below-floor and
    /// position-less rows).
    /// </summary>
    public long GamesAnalyzed { get; init; }

    /// <summary>Distinct champions with at least one ranked row on the active patch.</summary>
    public int ChampionsRanked { get; init; }

    /// <summary>
    /// The strongest rows on the patch, tier-then-score ordered (S first,
    /// strongest within a tier first), truncated to the requested limit.
    /// </summary>
    public IReadOnlyList<ChampionOverviewRowReadModel> TopRows { get; init; } = [];
}

/// <summary>One row of <see cref="ChampionOverviewReadModel.TopRows"/> — the homepage teaser's shape, not the full directory row.</summary>
public sealed record ChampionOverviewRowReadModel
{
    public int ChampionId { get; init; }

    public string Position { get; init; } = string.Empty;

    /// <summary>OPGG-style performance tier — see <see cref="ChampionSummaryReadModel.Tier"/>.</summary>
    public string Tier { get; init; } = string.Empty;

    public int Games { get; init; }

    public double WinRate { get; init; }

    public double PickRate { get; init; }

    /// <summary>Share of observed matches that banned this champion; null before #920's ban data. See <see cref="ChampionSummaryReadModel.BanRate"/>.</summary>
    public double? BanRate { get; init; }
}
