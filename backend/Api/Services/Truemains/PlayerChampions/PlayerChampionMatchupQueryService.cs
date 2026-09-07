using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Matchups;
using TrueMain.Services.Truemains.Identity;

namespace TrueMain.Services.Truemains.PlayerChampions;

/// <summary>
/// Player-scoped variant of the champion matchups query. Resolves the
/// <c>{gameName}-{tagLine}</c> name tag to a Riot account, then asks the shared
/// <see cref="Champions.Matchups.IChampionMatchupQueryService"/> for the same
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
        int? opponentChampionId,
        CancellationToken ct);
}

public sealed class PlayerChampionMatchupQueryService(
    TruemainAccountResolver resolver,
    IChampionMatchupQueryService matchupQueryService) : IPlayerChampionMatchupQueryService
{
    public async Task<ChampionMatchupsResponse?> GetAsync(
        string nameTag,
        int championId,
        string position,
        string? patch,
        int? opponentChampionId,
        CancellationToken ct)
    {
        var account = await resolver.ResolveAsync(nameTag, ct);
        if (account is null)
        {
            return null;
        }

        return await matchupQueryService.GetAsync(
            championId,
            position,
            patch,
            account.Id,
            opponentChampionId,
            // Player-scoped matchups are one player's own games — a rank filter is
            // meaningless there, so the elo bracket is always unfiltered (ALL).
            eloBracket: null,
            ct);
    }
}
