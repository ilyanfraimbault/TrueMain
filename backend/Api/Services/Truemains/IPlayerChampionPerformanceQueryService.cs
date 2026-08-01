using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

public interface IPlayerChampionPerformanceQueryService
{
    /// <summary>
    /// Aggregates TrueMain's per-match performance score over one player's
    /// recent ranked games on one champion. Null only when the name tag is
    /// malformed or the account is unknown — a known player with no games on
    /// the champion is a 200 carrying zero counts.
    /// </summary>
    /// <param name="nameTag">The player's Riot id in <c>Name-TAG</c> route form.</param>
    /// <param name="championId">The champion to scope the sample to.</param>
    /// <param name="patch">Major.minor patch filter, or null for every patch.</param>
    /// <param name="position">Canonical team position filter, or null for every lane.</param>
    /// <param name="ct">Request cancellation token.</param>
    Task<PlayerChampionPerformanceResponse?> GetAsync(
        string nameTag,
        int championId,
        string? patch,
        string? position,
        CancellationToken ct);
}
