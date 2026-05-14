using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public interface IChampionBuildTreeQueryService
{
    Task<ChampionBuildTreeReadModel> GetAsync(
        int championId,
        Guid? riotAccountId,
        string? patch,
        string? platformId,
        string? position,
        int maxDepth,
        int minBranchGames,
        CancellationToken ct);
}
