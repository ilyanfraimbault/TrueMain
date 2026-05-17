using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public interface IChampionBuildsQueryService
{
    /// <summary>
    /// Returns the top builds for a champion at a given patch + position,
    /// each build keyed by (first completed item, primary keystone) and
    /// carrying the four UI sections — core, variations, build tree, rune
    /// pages. <see langword="null"/> means the champion has no aggregated
    /// data on the active queue (404 territory).
    /// </summary>
    Task<ChampionResponse?> GetAsync(
        int championId,
        string? patch,
        string? position,
        CancellationToken ct);
}
