using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions;

namespace TrueMain.Services.Truemains;

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
