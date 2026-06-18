using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public interface IChampionItemTimingsQueryService
{
    Task<ChampionItemTimingsResponse> GetAsync(
        int championId,
        string position,
        string? patch,
        CancellationToken ct);
}
