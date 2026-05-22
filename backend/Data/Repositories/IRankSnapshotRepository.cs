using Data.Entities;

namespace Data.Repositories;

public interface IRankSnapshotRepository
{
    void Add(RankSnapshot snapshot);

    Task<RankSnapshot?> GetLatestAsync(Guid riotAccountId, CancellationToken ct);

    Task<Dictionary<Guid, RankSnapshot>> GetLatestForAccountsAsync(
        IReadOnlyCollection<Guid> riotAccountIds,
        CancellationToken ct);

    Task<List<RankSnapshot>> GetHistoryAsync(
        Guid riotAccountId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct);
}
