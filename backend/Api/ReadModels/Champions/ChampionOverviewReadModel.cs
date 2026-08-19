namespace TrueMain.ReadModels.Champions;

/// <summary>
/// Homepage-sized snapshot of the champion directory (<c>GET /champions/overview</c>,
/// #972): the all-time "games analyzed" total plus a short, pre-sorted slice of the
/// strongest rows, so the homepage never has to fetch and sort the full ~500-row
/// directory just to render a stat chip and an 8-row teaser. The teaser is scoped to
/// the active patch, no elo bracket filter — the homepage has none of its own.
/// </summary>
public sealed record ChampionOverviewReadModel
{
    /// <summary>
    /// Canonical <c>major.minor</c> patch <see cref="TopRows"/> was computed for —
    /// the patch the site serves. Not necessarily the newest patch with data: a patch
    /// too thin to fill a directory is skipped (#1109). Says nothing about
    /// <see cref="GamesAnalyzed"/>, which spans the whole aggregate history.
    /// </summary>
    public string PatchVersion { get; init; } = string.Empty;

    /// <summary>
    /// Every game the aggregate table holds for the tracked queue, all patches
    /// summed — the site's lifetime volume, not this patch's. Deliberately not
    /// patch-scoped: a figure the homepage prints without a qualifier must not fall
    /// every time a patch rolls over, and "how much has TrueMain measured" is the
    /// question the chip actually answers. Counts below-floor and position-less
    /// scopes too, like <see cref="ChampionSummariesResult.TotalGames"/> does per
    /// patch; not the sum of <see cref="TopRows"/> (a short slice).
    /// </summary>
    public long GamesAnalyzed { get; init; }

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
