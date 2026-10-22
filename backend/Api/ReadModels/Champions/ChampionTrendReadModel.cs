namespace TrueMain.ReadModels.Champions;

/// <summary>
/// Winrate / pickrate evolution for a single champion across recent patches
/// (<c>GET /champions/{id}/trend</c>). Powers the trend chart on the champion
/// detail page so users can see whether a champion is climbing or sliding.
/// The series is pinned to one position — the requested one, or the
/// champion's dominant lane on the latest patch when unspecified — so every
/// point in <see cref="Points"/> is comparable.
/// </summary>
public sealed record ChampionTrendReadModel
{
    public int ChampionId { get; init; }

    /// <summary>
    /// Position every point is computed for. Empty string when the champion
    /// has no positioned rows in the record (the series is then empty too).
    /// </summary>
    public string Position { get; init; } = string.Empty;

    /// <summary>
    /// One entry per patch that has data for this <c>(champion, position)</c>,
    /// ordered oldest → newest so the chart's X axis reads left-to-right in
    /// release order.
    /// </summary>
    public IReadOnlyList<ChampionTrendPoint> Points { get; init; } = [];
}

/// <summary>
/// A single patch's aggregated rates for the champion trend series. Every
/// rate is a fraction in <c>[0, 1]</c>, derived from the light per-patch record
/// (<c>champion_patch_stats</c> + the ban pair, #1957) — the scopes the directory
/// and detail endpoints read, summed per patch, and still there once a patch's
/// full aggregates are purged; never synthesised. <see cref="Games"/> is surfaced
/// so the frontend can flag thin patches.
/// </summary>
public sealed record ChampionTrendPoint
{
    public string Patch { get; init; } = string.Empty;

    public double WinRate { get; init; }

    /// <summary>
    /// Share of TrueMain games on this position taken by this champion on the
    /// patch — the same main-population pickrate definition used by
    /// <see cref="ChampionSummaryReadModel.PickRate"/>.
    /// </summary>
    public double PickRate { get; init; }

    /// <summary>
    /// Share of the patch's observed matches that banned this champion, across all
    /// elo bands unless the request carries a rank filter. <see langword="null"/>
    /// on any patch older than ban ingestion (#920) — see
    /// <see cref="ChampionSummaryReadModel.BanRate"/>. Early on, most of the series
    /// is null, so consumers must handle a partly-null series rather than assume the
    /// column is present on every point.
    /// </summary>
    public double? BanRate { get; init; }

    public int Games { get; init; }
}
