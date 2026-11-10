using Data.Entities;

namespace Data.Repositories;

public interface IRankSnapshotRepository
{
    /// <summary>
    /// Stages <paramref name="snapshot"/> for the session's next save, which writes it as an
    /// upsert on the account's UTC day: when another writer already stored that day, the newer
    /// of the two readings is kept instead of the save failing on the one-row-per-day index.
    /// </summary>
    void Add(RankSnapshot snapshot);

    void Update(RankSnapshot snapshot);

    Task<RankSnapshot?> GetLatestAsync(Guid riotAccountId, CancellationToken ct);

    Task<Dictionary<Guid, RankSnapshot>> GetLatestForAccountsAsync(
        IReadOnlyCollection<Guid> riotAccountIds,
        CancellationToken ct);

    /// <summary>
    /// Every capture of <paramref name="riotAccountIds"/> as (capture time, tier) per account —
    /// the input of <c>EloBracketResolver.FromNearestSnapshot</c>. Accounts without a capture
    /// are absent from the result.
    /// </summary>
    Task<Dictionary<Guid, List<(DateTime CapturedAtUtc, string? Tier)>>> GetTierHistoryForAccountsAsync(
        IReadOnlyCollection<Guid> riotAccountIds,
        CancellationToken ct);

    Task<List<RankSnapshot>> GetHistoryAsync(
        Guid riotAccountId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct);
}
