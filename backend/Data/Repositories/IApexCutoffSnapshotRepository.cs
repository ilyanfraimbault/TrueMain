using Core.Lol.Ranking;

namespace Data.Repositories;

public interface IApexCutoffSnapshotRepository
{
    /// <summary>
    /// Insert or replace the platform's cutoff for <paramref name="tier"/> on the UTC day of
    /// <paramref name="nowUtc"/>. Written immediately with a single statement, so it does not
    /// need (and is not affected by) a later SaveChanges.
    /// </summary>
    Task UpsertAsync(string platformId, RankTier tier, int minLeaguePoints, int entryCount, DateTime nowUtc, CancellationToken ct);

    /// <summary>
    /// Estimates every (platform, tier, UTC day) from <paramref name="fromUtc"/> to the day before
    /// <paramref name="nowUtc"/> that has no row yet, from the rank snapshots of that day (#2181).
    /// Runs once: a no-op as soon as any estimate exists. Returns the rows written.
    /// </summary>
    Task<int> EstimateHistoryAsync(DateTime fromUtc, DateTime nowUtc, int minSnapshots, CancellationToken ct);
}
