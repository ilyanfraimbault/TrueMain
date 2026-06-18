using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public interface IChampionScalingQueryService
{
    Task<ChampionScalingResponse> GetAsync(
        int championId,
        string position,
        string? patch,
        CancellationToken ct);
}
