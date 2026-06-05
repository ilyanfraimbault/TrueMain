namespace TrueMain.Services.Champions;

/// <summary>
/// Optional narrowing for <see cref="IChampionBuildsQueryService.GetAsync"/>.
/// Pins the champion-build aggregate to a single player so the build path,
/// runes, skill order and winrate come only from that player's games on the
/// champion rather than the global pool. The aggregate schema already keys
/// every scope row on the account, so this is a pure filter — the read model
/// returned is byte-for-byte identical to the global response.
/// </summary>
/// <param name="RiotAccountId">Resolved account whose games to scope to.</param>
/// <param name="PlatformId">
/// Riot platform of the account (e.g. <c>EUW1</c>). Pinned alongside the
/// account id so the SQL filter hits the composite scope index.
/// </param>
/// <param name="MinGames">
/// Minimum number of games (across the resolved patch + position) required to
/// return a payload. Below this floor the service returns <see langword="null"/>
/// so the page renders a "not enough games" notice instead of a sparse,
/// misleading build. <c>0</c> disables the floor (global callers).
/// </param>
public sealed record ChampionBuildsScope(
    Guid RiotAccountId,
    string PlatformId,
    int MinGames);
