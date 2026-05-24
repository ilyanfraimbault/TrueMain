using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

public interface ITruemainsLeaderboardQueryService
{
    /// <summary>
    /// Returns a paged slice of the truemains leaderboard. The list is
    /// restricted to accounts on the regions the leaderboard exposes
    /// (<c>europe</c> / <c>americas</c> / <c>korea</c>) and to those that
    /// have at least one rank snapshot — unranked accounts are out of scope
    /// for V1. Optional filters narrow by region, dominant role of a main
    /// champion, and/or a specific champion id.
    /// </summary>
    Task<LeaderboardResponse> GetAsync(
        int page,
        int pageSize,
        string? region,
        string? position,
        int? championId,
        CancellationToken ct);
}
