using Data.Entities;

namespace Data.Repositories;

public interface IPaceSampledMatchRepository
{
    /// <summary>Those of <paramref name="matchIds"/> already in the ledger.</summary>
    Task<HashSet<string>> GetSampledAsync(IReadOnlyCollection<string> matchIds, CancellationToken ct);

    /// <summary>Stages a ledger row; written by the next <c>SaveChangesAsync</c>.</summary>
    void Add(PaceSampledMatch match);

    /// <summary>Deletes the rows sampled before <paramref name="cutoffUtc"/> and returns how many.</summary>
    Task<int> DeleteSampledBeforeAsync(DateTime cutoffUtc, CancellationToken ct);
}
