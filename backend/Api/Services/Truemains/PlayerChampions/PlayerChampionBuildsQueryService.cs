using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Builds;
using TrueMain.Services.Champions.Scopes;
using TrueMain.Services.Truemains.Identity;

namespace TrueMain.Services.Truemains.PlayerChampions;

/// <summary>
/// Player-scoped variant of the champion builds query. Resolves the
/// <c>{gameName}-{tagLine}</c> name tag to a Riot account, then asks the
/// shared <see cref="Champions.Builds.IChampionBuildsQueryService"/> for the same
/// <see cref="ChampionResponse"/> the global champion page consumes — only
/// every aggregate is computed from that player's games on the champion.
/// </summary>
public interface IPlayerChampionBuildsQueryService
{
    /// <summary>
    /// Returns the player-scoped builds for <paramref name="championId"/>.
    /// <see langword="null"/> means either the name tag is malformed, no
    /// account matches, or the player has fewer than the configured minimum
    /// games on the champion at the resolved patch + position — all of which
    /// the controller maps to 404 and the page renders as an empty state.
    /// </summary>
    Task<ChampionResponse?> GetAsync(
        string nameTag,
        int championId,
        string? patch,
        string? position,
        CancellationToken ct);
}

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
            new ChampionBuildsScope(account.Id, account.PlatformId, MinPlayerGames),
            ct: ct);
    }
}
