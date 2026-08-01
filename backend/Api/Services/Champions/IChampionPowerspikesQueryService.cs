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
    ///
    /// <para>
    /// <paramref name="opponentChampionId"/> narrows the spikes to the games played
    /// against that lane opponent (#957), mirroring the champion page's matchup
    /// filter. Omitted (or non-positive), the read sums across every opponent and
    /// returns exactly what it returned before the dimension existed.
    /// </para>
    /// </summary>
    Task<ChampionPowerspikesResponse> GetAsync(
        int championId,
        string position,
        string? patch,
        string? eloBracket,
        int buildFirstItemId,
        int buildKeystoneId,
        int? opponentChampionId,
        CancellationToken ct);
}
