using Data.Entities;

namespace Data.Repositories;

public interface IMatchWinProbabilityRepository
{
    void Add(MatchWinProbability winProbability);

    /// <summary>
    /// Clears the win-probability rows of every match in <paramref name="matchIds"/> in one
    /// statement, so a re-ingested timeline replaces its row (set-based per batch, as the
    /// timeline snapshots' delete, #1229).
    /// </summary>
    Task<int> DeleteByMatchIdsAsync(IReadOnlyCollection<string> matchIds, CancellationToken ct);
}
