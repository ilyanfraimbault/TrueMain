using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions;

namespace TrueMain.Services.Truemains;

public sealed class PlayerChampionBuildsQueryService(
    TruemainAccountResolver resolver,
    IChampionBuildsQueryService buildsQueryService) : IPlayerChampionBuildsQueryService
{
    /// <summary>
    /// Preferred minimum games on the champion (at a single patch + position)
    /// when resolving which patch to render. The loader picks the most recent
    /// patch that clears this floor so a thin newest patch doesn't shadow a
    /// meaningful earlier one. It is a *preference only* — a champion the
    /// player has genuinely played still renders a (thin, low-confidence)
    /// build rather than 404-ing, so a main listed on the profile never
    /// dead-ends on click. Five is the smallest sample where a dominant build
    /// path starts to mean something rather than echoing a single game.
    /// </summary>
    public const int MinPlayerGames = 5;

    public async Task<ChampionResponse?> GetAsync(
        string nameTag,
        int championId,
        string? patch,
        string? position,
        CancellationToken ct)
    {
        var account = await resolver.ResolveAsync(nameTag, ct);
        if (account is null)
        {
            return null;
        }

        return await buildsQueryService.GetAsync(
            championId,
            patch,
            position,
            ct,
            new ChampionBuildsScope(account.Id, account.PlatformId, MinPlayerGames));
    }
}
