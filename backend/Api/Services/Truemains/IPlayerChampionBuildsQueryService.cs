using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Truemains;

/// <summary>
/// Player-scoped variant of the champion builds query. Resolves the
/// <c>{gameName}-{tagLine}</c> name tag to a Riot account, then asks the
/// shared <see cref="Champions.IChampionBuildsQueryService"/> for the same
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
