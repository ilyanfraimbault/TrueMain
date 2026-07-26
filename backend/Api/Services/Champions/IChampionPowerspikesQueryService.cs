using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public interface IChampionPowerspikesQueryService
{
    /// <summary>
    /// Event spikes for a champion at a position, scoped to one core build
    /// (#890): the items that build completes and the level milestones
    /// (6/11/16), each carrying how much the power curve accelerates around it.
    /// <paramref name="buildFirstItemId"/> / <paramref name="buildKeystoneId"/>
    /// identify the core build the same way the builds read keys its tabs.
    /// Same queue / patch / tracked-account population as the sibling champion
    /// reads. Returns an empty event list when the slice has no data yet.
    /// </summary>
    Task<ChampionPowerspikesResponse> GetAsync(
        int championId,
        string position,
        string? patch,
        string? eloBracket,
        int buildFirstItemId,
        int buildKeystoneId,
        CancellationToken ct);
}
