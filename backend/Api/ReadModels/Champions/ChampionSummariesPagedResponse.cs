namespace TrueMain.ReadModels.Champions;

/// <summary>
/// Paged envelope around <see cref="ChampionSummaryReadModel"/> returned by
/// <c>GET /champions</c>. <see cref="Items"/> is the requested slice;
/// <see cref="TotalCount"/> is the unfiltered length of the in-cache list
/// (the full directory for the active patch) so the frontend can size its
/// <c>UPagination</c> without a separate count round-trip.
/// </summary>
public sealed class ChampionSummariesPagedResponse
{
    /// <summary>The slice of summaries for the requested page.</summary>
    public IReadOnlyList<ChampionSummaryReadModel> Items { get; init; } = [];

    /// <summary>Total number of summaries available across all pages.</summary>
    public int TotalCount { get; init; }

    /// <summary>1-indexed page number of <see cref="Items"/>.</summary>
    public int Page { get; init; }

    /// <summary>Page size used to produce <see cref="Items"/>.</summary>
    public int PageSize { get; init; }
}
