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
    RankSnapshotOutcome Write(
        IDataSession session,
        RiotAccount account,
        RankSnapshotInput input,
        RankSnapshot? latest,
        DateTime nowUtc);
}

public sealed class RankSnapshotWriter : IRankSnapshotWriter
{
    public RankSnapshotOutcome Write(
        IDataSession session,
        RiotAccount account,
        RankSnapshotInput input,
        RankSnapshot? latest,
        DateTime nowUtc)
    {
        account.LastRankSyncAtUtc = nowUtc;
        // Denormalised leaderboard sort key — kept in lock-step with the latest
        // rank so the leaderboard can ORDER BY it without recomputing in SQL.
        // Idempotent: EF only writes when the value actually changes.
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
