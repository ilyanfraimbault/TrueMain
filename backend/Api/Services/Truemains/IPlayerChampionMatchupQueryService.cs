using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Truemains;

/// <summary>
/// Player-scoped variant of the champion matchup query. Resolves the
/// <c>{gameName}-{tagLine}</c> name tag to a Riot account, then asks the shared
/// <see cref="Champions.IChampionMatchupQueryService"/> for the same
/// <see cref="ChampionMatchupResponse"/> the global matchup endpoint returns —
/// only the slice is narrowed to that player's games on the champion.
/// </summary>
public interface IPlayerChampionMatchupQueryService
{
    /// <summary>
    /// Returns the player-scoped lane matchup for <paramref name="championId"/>
    /// against <paramref name="opponentChampionId"/>. <see langword="null"/>
    /// means the name tag is malformed, no account matches, or the player has
    /// fewer than the configured minimum games in that matchup — all of which
    /// the controller maps to 404.
    /// </summary>
    Task<ChampionMatchupResponse?> GetAsync(
        string nameTag,
        int championId,
        string position,
        int opponentChampionId,
        string? patch,
        CancellationToken ct);
}
