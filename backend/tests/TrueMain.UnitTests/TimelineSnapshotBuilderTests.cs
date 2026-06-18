using AwesomeAssertions;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Riot.Dto;

namespace TrueMain.UnitTests;

/// <summary>
/// Covers per-interval snapshot extraction (issue #525): frame selection at the
/// fixed minute marks, and cumulative kill/ward tallies up to each frame.
/// </summary>
public sealed class TimelineSnapshotBuilderTests
{
    private const string MatchId = "EUW1_1";

    [Fact]
    public void Build_EmitsRowsForReachedMinuteMarks_WithCumulativeTallies()
    {
        var timeline = new MatchTimelineDto
        {
            Frames =
            [
                Frame(0, Participant(1, gold: 500), Participant(2, gold: 500)),
                Frame(300_000, Participant(1, gold: 1500, cs: 30, jungleCs: 5, level: 6, xp: 3000, dmg: 2000), Participant(2, gold: 1400)),
                Frame(600_000, Participant(1, gold: 4000, cs: 70, level: 11, dmg: 8000), Participant(2, gold: 3500))
            ],
            Events =
            [
                new MatchTimelineEventDto { Type = "CHAMPION_KILL", TimestampMs = 250_000, KillerId = 1 },
                new MatchTimelineEventDto { Type = "CHAMPION_KILL", TimestampMs = 400_000, KillerId = 1 },
                new MatchTimelineEventDto { Type = "WARD_PLACED", TimestampMs = 200_000, CreatorId = 1 },
                new MatchTimelineEventDto { Type = "WARD_KILL", TimestampMs = 350_000, KillerId = 1 }
            ]
        };

        var snapshots = TimelineSnapshotBuilder.Build(MatchId, timeline);

        // Only the 5- and 10-minute marks have a frame within tolerance; 15/20/30 do not.
        snapshots.Select(s => s.IntervalMinute).Distinct().Should().BeEquivalentTo([5, 10]);
        snapshots.Should().HaveCount(4); // 2 participants x 2 intervals
        snapshots.Should().OnlyContain(s => s.MatchId == MatchId);

        var p1At5 = snapshots.Single(s => s.ParticipantId == 1 && s.IntervalMinute == 5);
        p1At5.TimestampMs.Should().Be(300_000);
        p1At5.TotalGold.Should().Be(1500);
        p1At5.MinionsKilled.Should().Be(30);
        p1At5.JungleMinionsKilled.Should().Be(5);
        p1At5.Level.Should().Be(6);
        p1At5.Xp.Should().Be(3000);
        p1At5.DamageToChampions.Should().Be(2000);
        p1At5.Kills.Should().Be(1);        // kill at 250s counted, 400s not yet
        p1At5.WardsPlaced.Should().Be(1);  // ward at 200s
        p1At5.WardsKilled.Should().Be(0);  // ward kill at 350s not yet

        var p1At10 = snapshots.Single(s => s.ParticipantId == 1 && s.IntervalMinute == 10);
        p1At10.TotalGold.Should().Be(4000);
        p1At10.Kills.Should().Be(2);       // both kills now counted
        p1At10.WardsKilled.Should().Be(1); // ward kill at 350s now counted
    }

    [Fact]
    public void Build_ReturnsEmpty_WhenNoFrames()
        => TimelineSnapshotBuilder.Build(MatchId, new MatchTimelineDto()).Should().BeEmpty();

    [Theory]
    [InlineData(330_000, true)]  // exactly +30s from the 5-minute mark -> within tolerance
    [InlineData(330_001, false)] // 1ms past tolerance -> no snapshot
    public void Build_RespectsFrameToleranceBoundary(int frameTimestampMs, bool expectSnapshot)
    {
        var timeline = new MatchTimelineDto { Frames = [Frame(frameTimestampMs, Participant(1, gold: 100))] };

        var snapshots = TimelineSnapshotBuilder.Build(MatchId, timeline);

        snapshots.Any(s => s.IntervalMinute == 5).Should().Be(expectSnapshot);
    }

    private static MatchTimelineFrameDto Frame(int timestampMs, params MatchParticipantFrameDto[] participants)
        => new() { TimestampMs = timestampMs, ParticipantFrames = [.. participants] };

    private static MatchParticipantFrameDto Participant(
        int participantId, int gold = 0, int cs = 0, int jungleCs = 0, int level = 1, int xp = 0, int dmg = 0)
        => new()
        {
            ParticipantId = participantId,
            TotalGold = gold,
            MinionsKilled = cs,
            JungleMinionsKilled = jungleCs,
            Level = level,
            Xp = xp,
            TotalDamageToChampions = dmg
        };
}
