using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public interface IChampionFoundationQueryService
{
    Task<ChampionFoundationReadModel?> GetAsync(
        int championId,
        Guid? riotAccountId,
        string? patch,
        string? platformId,
        string? position,
        CancellationToken ct);

    /// <summary>
    /// Phase 6.3 — same as the no-pivot overload but filters the underlying
    /// pattern set so per-dimension stats answer "given this build, what
    /// runes / skill order / spells / starters do players run". Pass
    /// <see cref="ChampionPatternPivot.None"/> to get the unconditional view.
    /// </summary>
    Task<ChampionFoundationReadModel?> GetAsync(
        int championId,
        Guid? riotAccountId,
        string? patch,
        string? platformId,
        string? position,
        ChampionPatternPivot pivot,
        CancellationToken ct);

    /// <summary>
    /// Lightweight directory query: one <see cref="ChampionSummaryReadModel"/>
    /// per champion that has data on the active queue, each summary computed
    /// against the champion's own latest patch (no global patch pin). Used by
    /// list / index pages that need games + winRate + dominant position
    /// without paying for builds, runes or patterns.
    /// </summary>
    Task<IReadOnlyList<ChampionSummaryReadModel>> GetAllSummariesAsync(CancellationToken ct);
}
