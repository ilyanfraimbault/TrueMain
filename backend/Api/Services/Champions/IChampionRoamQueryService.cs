using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public interface IChampionRoamQueryService
{
    Task<ChampionRoamResponse> GetAsync(
        int championId,
        string position,
        string? patch,
        CancellationToken ct);
}
