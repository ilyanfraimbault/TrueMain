using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public interface IChampionTimelineLeadsQueryService
{
    Task<ChampionTimelineLeadsResponse> GetAsync(
        int championId,
        string position,
        string? patch,
        string? eloBracket,
        CancellationToken ct);
}
