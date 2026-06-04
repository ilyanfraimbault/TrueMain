using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Truemains;

/// <summary>
/// Player-scoped variant of the champion matchups query. Resolves the
/// <c>{gameName}-{tagLine}</c> name tag to a Riot account, then asks the shared
/// <see cref="Champions.IChampionMatchupQueryService"/> for the same
/// <see cref="ChampionMatchupsResponse"/> the global matchups endpoint returns —
/// only the slice is narrowed to that player's games on the champion.
/// </summary>
public interface IPlayerChampionMatchupQueryService
{
    /// <summary>
    /// Returns the player-scoped lane matchups for <paramref name="championId"/>
    /// at <paramref name="position"/>. <see langword="null"/> means the name tag
    /// is malformed or no account matches — the controller maps that to 404. A
    /// known player with no opponents above the minimum-games floor yields a
    /// non-null response with an empty list (a 200), mirroring the global route.
    /// </summary>
    Task<ChampionMatchupsResponse?> GetAsync(
        string nameTag,
        int championId,
        string position,
        string? patch,
        CancellationToken ct);
}
