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
}
