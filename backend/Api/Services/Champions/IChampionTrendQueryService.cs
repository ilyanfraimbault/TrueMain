using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public interface IChampionTrendQueryService
{
    /// <summary>
    /// Builds the per-patch winrate / pickrate series for a champion on a
    /// single position, spanning up to the most recent patches that have
    /// data (oldest → newest). The position is the requested one when given
    /// and canonical, otherwise the champion's dominant lane on its latest
    /// patch. Returns an empty series (not null) when the champion has no
    /// aggregate scopes, so the caller can render a "not enough data" state
    /// without a 404.
    /// </summary>
    Task<ChampionTrendReadModel> GetTrendAsync(
        int championId,
        string? position,
        CancellationToken ct);
}
