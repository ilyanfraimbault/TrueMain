using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public interface IChampionFoundationQueryService
{
    Task<ChampionFoundationReadModel?> GetAsync(int championId, CancellationToken ct);
}
