using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public interface IChampionStatsQueryService
{
    Task<IReadOnlyList<ChampionStatRow>> GetAsync(
        string? region,
        string? patch,
        string? position,
        int? queue,
        CancellationToken ct);
}
