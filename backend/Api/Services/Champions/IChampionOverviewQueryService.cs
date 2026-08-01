using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public interface IChampionOverviewQueryService
{
    /// <summary>
    /// Homepage snapshot (#972): the active patch's true games-analyzed total
    /// plus the <paramref name="limit"/> strongest rows, tier-then-games
    /// ordered. No patch or elo parameter — the homepage always reads the
    /// active patch, unfiltered, same as an unqualified <c>GET /champions</c>.
    /// </summary>
    /// <param name="limit">Row count for <see cref="ChampionOverviewReadModel.TopRows"/>; caller clamps to a sane range.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ChampionOverviewReadModel> GetOverviewAsync(int limit, CancellationToken ct);
}
