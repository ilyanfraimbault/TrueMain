using Core.Lol.Ranking;
using Data.Entities;
using Data.Repositories;

namespace Ingestor.Ranking;

public sealed record RankSnapshotInput(string Tier, string Division, int LeaguePoints, int? Wins, int? Losses);

public enum RankSnapshotOutcome
{
    Inserted,
    Updated,
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
    /// must move forward even when the rank is unchanged. At most one
    /// <see cref="RankSnapshot"/> row is kept per account per UTC calendar day: an
    /// unchanged rank writes nothing (<see cref="RankSnapshotOutcome.Unchanged"/>), a
    /// changed rank on the same day as <paramref name="latest"/> overwrites that row
    /// in place (<see cref="RankSnapshotOutcome.Updated"/>), and a changed rank on a
    /// new day appends a fresh row (<see cref="RankSnapshotOutcome.Inserted"/>).
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

        // Kept current on every reading, including the Unchanged path (#1360): the claim
        // orders by how many games this account has played since we last ingested it, and
        // that difference is only meaningful if the left-hand side tracks the ladder. A rank
        // that is "unchanged" in tier/division/LP can still sit on a different game count —
        // a win and a loss return to the same LP — and those are exactly the games the claim
        // would otherwise never learn about.
        if (input.Wins is { } wins && input.Losses is { } losses)
        {
            account.LadderGames = wins + losses;
        }

        var unchanged = latest is not null
            && string.Equals(latest.Tier, input.Tier, StringComparison.Ordinal)
            && string.Equals(latest.Division, input.Division, StringComparison.Ordinal)
            && latest.LeaguePoints == input.LeaguePoints;

        if (unchanged)
        {
            return RankSnapshotOutcome.Unchanged;
        }

        // Cap storage at one snapshot per account per day: a rank change on the
        // same UTC day as the latest row overwrites it instead of appending, so a
        // player who climbs several times in one day still leaves a single row.
        if (latest is not null && latest.CapturedAtUtc.Date == nowUtc.Date)
        {
            latest.CapturedAtUtc = nowUtc;
            latest.Tier = input.Tier;
            latest.Division = input.Division;
            latest.LeaguePoints = input.LeaguePoints;
            latest.Wins = input.Wins;
            latest.Losses = input.Losses;

            session.RankSnapshots.Update(latest);

            return RankSnapshotOutcome.Updated;
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
