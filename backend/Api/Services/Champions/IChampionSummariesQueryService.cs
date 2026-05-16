using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public interface IChampionSummariesQueryService
{
    /// <summary>
    /// Lightweight directory query: one <see cref="ChampionSummaryReadModel"/>
    /// per champion that has data on the active queue, each summary computed
    /// against the champion's own latest patch (no global patch pin). Used by
    /// list / index pages that need games + winRate + dominant position
    /// without paying for builds, runes or patterns.
    /// </summary>
    Task<IReadOnlyList<ChampionSummaryReadModel>> GetAllSummariesAsync(CancellationToken ct);
}
