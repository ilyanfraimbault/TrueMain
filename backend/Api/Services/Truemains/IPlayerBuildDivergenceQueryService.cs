using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

/// <summary>
/// "You vs mains" comparison for the player-scoped champion page: how one
/// player's dominant starter / boots / build path / skill order line up against
/// what the champion's other mains do at the same patch and position.
/// </summary>
public interface IPlayerBuildDivergenceQueryService
{
    /// <summary>
    /// Returns the comparison for <paramref name="championId"/>.
    /// <see langword="null"/> means the name tag is malformed, no account
    /// matches, or the player has no aggregate at all on the champion — all of
    /// which the controller maps to 404.
    ///
    /// A player with an aggregate but too thin a sample (or a champion + lane
    /// with too few mains to compare against) is <em>not</em> an error: the
    /// response comes back with the sample flags false and no dimensions, so
    /// the page can say why instead of pretending the data is missing.
    /// </summary>
    Task<PlayerBuildDivergenceResponse?> GetAsync(
        string nameTag,
        int championId,
        string? patch,
        string? position,
        CancellationToken ct);
}
