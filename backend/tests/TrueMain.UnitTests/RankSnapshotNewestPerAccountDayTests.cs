using AwesomeAssertions;
using Core.Lol.Ranking;
using Data.Entities;
using Ingestor.Persistence;

namespace TrueMain.UnitTests;

/// <summary>
/// #2587: staged snapshots are written as one upsert on the account's UTC day, and one
/// statement cannot touch the same row twice — so the batch keeps the newest reading per day.
/// </summary>
public sealed class RankSnapshotNewestPerAccountDayTests
{
    private static readonly DateTime Morning = new(2026, 11, 9, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void KeepsTheNewestReadingOfEachAccountDay()
    {
        var account = Guid.NewGuid();
        var other = Guid.NewGuid();

        var kept = RankSnapshotRepository.NewestPerAccountDay(
        [
            Snapshot(account, Morning, 40),
            Snapshot(account, Morning.AddHours(3), 60),
            Snapshot(account, Morning.AddDays(-1), 20),
            Snapshot(other, Morning, 10),
        ]);

        kept.Select(s => (s.RiotAccountId, s.LeaguePoints))
            .Should().BeEquivalentTo([(account, 60), (account, 20), (other, 10)]);
    }

    [Fact]
    public void ReadsALocalTimeOnItsUtcDay()
    {
        var account = Guid.NewGuid();
        var lateUtc = new DateTime(2026, 11, 9, 23, 30, 0, DateTimeKind.Utc);

        var kept = RankSnapshotRepository.NewestPerAccountDay(
        [
            Snapshot(account, lateUtc.ToLocalTime(), 40),
            Snapshot(account, lateUtc.AddMinutes(-10), 30),
        ]);

        kept.Should().ContainSingle().Which.LeaguePoints.Should().Be(40);
    }

    private static RankSnapshot Snapshot(Guid accountId, DateTime capturedAtUtc, int leaguePoints) => new()
    {
        Id = Guid.NewGuid(),
        RiotAccountId = accountId,
        CapturedAtUtc = capturedAtUtc,
        Tier = RankTier.Master,
        Division = RankDivision.I,
        LeaguePoints = leaguePoints,
    };
}
