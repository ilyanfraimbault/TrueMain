using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public interface IChampionPowerspikesQueryService
{
    /// <summary>
    /// Power curve + event spikes for a champion at a position. Live-computed
    /// from per-minute timeline snapshots (issue #567) and item events; same
    /// queue / patch / tracked-account population as the sibling champion reads.
    /// Returns an empty curve / events list when the slice has no data yet
    /// (the per-minute data is forward-only and fills as games are ingested).
    /// </summary>
    Task<ChampionPowerspikesResponse> GetAsync(
        int championId,
        string position,
        string? patch,
        CancellationToken ct);
}
