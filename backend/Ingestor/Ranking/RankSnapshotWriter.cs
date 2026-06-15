using Core.Lol.Ranking;
using Data.Entities;
using Data.Repositories;

namespace Ingestor.Ranking;

public sealed record RankSnapshotInput(string Tier, string Division, int LeaguePoints, int? Wins, int? Losses);

public enum RankSnapshotOutcome
{
    Inserted,
    Unchanged
}

public interface IRankSnapshotWriter
{
    /// <summary>
    /// Ingests a fresh rank reading for <paramref name="account"/>.
    /// </summary>
    /// <remarks>
    /// Every successful reading advances the account's sync bookkeeping —
    /// <see cref="RiotAccount.LastRankSyncAtUtc"/> and <see cref="RiotAccount.Score"/> —
    /// regardless of the returned <see cref="RankSnapshotOutcome"/>. This is
    /// intentional and not a no-op-path accident: <c>LastRankSyncAtUtc</c> is the
    /// freshness gate that stops <c>AccountRefreshProcess</c> from re-issuing the
    /// League-v4 by-puuid call (and dedups against <c>DiscoveryProcess</c>), so it
    /// must move forward even when the rank is unchanged. A new
    /// <see cref="RankSnapshot"/> row is appended only when the rank actually
    /// changed (<see cref="RankSnapshotOutcome.Inserted"/>).
    /// </remarks>
    RankSnapshotOutcome Ingest(
        IDataSession session,
        RiotAccount account,
        RankSnapshotInput input,
        RankSnapshot? latest,
        DateTime nowUtc);
}

public sealed class RankSnapshotWriter : IRankSnapshotWriter
{
    public RankSnapshotOutcome Ingest(
        IDataSession session,
        RiotAccount account,
        RankSnapshotInput input,
        RankSnapshot? latest,
        DateTime nowUtc)
    {
        // A fresh reading always advances the account's sync bookkeeping, even on
        // the Unchanged path: LastRankSyncAtUtc gates redundant League-v4 by-puuid
        // calls (see AccountRefreshProcess), and Score is the denormalised
        // leaderboard sort key kept in lock-step with the latest rank so the
        // leaderboard can ORDER BY it without recomputing in SQL. Both are
        // idempotent — EF only writes when the value actually changes.
        account.LastRankSyncAtUtc = nowUtc;
        account.Score = RankScore.Compute(input.Tier, input.Division, input.LeaguePoints);

        var unchanged = latest is not null
            && string.Equals(latest.Tier, input.Tier, StringComparison.Ordinal)
            && string.Equals(latest.Division, input.Division, StringComparison.Ordinal)
            && latest.LeaguePoints == input.LeaguePoints;

        if (unchanged)
        {
            return RankSnapshotOutcome.Unchanged;
        }

        session.RankSnapshots.Add(new RankSnapshot
        {
            Id = Guid.NewGuid(),
            RiotAccountId = account.Id,
            CapturedAtUtc = nowUtc,
            Tier = input.Tier,
            Division = input.Division,
            LeaguePoints = input.LeaguePoints,
            Wins = input.Wins,
            Losses = input.Losses,
        });

        return RankSnapshotOutcome.Inserted;
    }
}
