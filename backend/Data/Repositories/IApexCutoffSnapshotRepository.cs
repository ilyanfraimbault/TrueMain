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
}
