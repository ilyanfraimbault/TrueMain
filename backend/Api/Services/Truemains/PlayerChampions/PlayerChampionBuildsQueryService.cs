using Microsoft.Extensions.Options;
using TrueMain.Options;
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
    IChampionBuildsQueryService buildsQueryService,
    IOptions<ChampionsListOptions> championsOptions) : IPlayerChampionBuildsQueryService
{
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
            new ChampionBuildsScope(account.Id, account.PlatformId, championsOptions.Value.MinPlayerBuildGames),
            ct: ct);
    }
}
