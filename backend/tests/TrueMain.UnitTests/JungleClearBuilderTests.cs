using AwesomeAssertions;
using Core.Lol.Map;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Riot.Dto;

namespace TrueMain.UnitTests;

/// <summary>
/// Covers first-clear measurement (#1188, replacing #535's camp-sequence
/// reconstruction): jungler identification, start-camp detection from the
/// jungle-CS-still-zero frames, the clear-speed samples, and the full-clear
/// threshold crossing.
///
/// The frames here model the real game: buffs spawn at 1:30, so minute 1 has 0
/// jungle CS and a jungler is at ~12 by minute 2 and ~20 by minute 3 — three to
/// four camps inside a single frame, which is exactly why no camp order is
/// claimed anywhere in this builder.
/// </summary>
public sealed class JungleClearBuilderTests
{
    private const string MatchId = "EUW1_1";

    [Fact]
    public void Build_MeasuresARealisticFirstClear()
    {
        // A standard clear: nothing at 1:00 (waiting on red buff), three camps
        // (12 CS) by 2:00, all six (24 CS) by 3:00.
        var frames = new List<MatchTimelineFrameDto>
        {
            Frame(0, JunglerAt(1, JungleCamp.BlueRedBuff, jungleCs: 0)),
            Frame(60_000, JunglerAt(1, JungleCamp.BlueRedBuff, jungleCs: 0)),
            Frame(120_000, JunglerAt(1, JungleCamp.BlueRaptors, jungleCs: 12)),
            Frame(180_000, JunglerAt(1, JungleCamp.BlueGromp, jungleCs: 24)),
        };

        var clears = JungleClearBuilder.Build(MatchId, new MatchTimelineDto { Frames = frames });

        clears.Should().ContainSingle();
        var clear = clears[0];
        clear.MatchId.Should().Be(MatchId);
        clear.ParticipantId.Should().Be(1);
        clear.StartCamp.Should().Be(nameof(JungleCamp.BlueRedBuff));
        clear.FullClearTimeMs.Should().Be(180_000);
        clear.Samples.Select(s => s.JungleCs).Should().Equal(0, 0, 12, 24);
        clear.Samples.Select(s => s.TimestampMs).Should().Equal(0, 60_000, 120_000, 180_000);
    }

    [Fact]
    public void Build_StartCamp_ComesFromTheLastFrameWithNoJungleCs()
    {
        // The t=0 frame catches the jungler still near the fountain; the 1:00
        // frame catches him waiting on the camp he opens. The later one wins.
        var frames = new List<MatchTimelineFrameDto>
        {
            Frame(0, Jungler(1, x: 560, y: 590, jungleCs: 0)),
            Frame(60_000, JunglerAt(1, JungleCamp.BlueBlueBuff, jungleCs: 0)),
            Frame(120_000, JunglerAt(1, JungleCamp.BlueWolves, jungleCs: 11)),
        };

        var clear = JungleClearBuilder.Build(MatchId, new MatchTimelineDto { Frames = frames }).Single();

        clear.StartCamp.Should().Be(nameof(JungleCamp.BlueBlueBuff));
    }

    [Fact]
    public void Build_StartCamp_IsNullWhenNoZeroCsFrameSitsOnACamp()
    {
        // Only ever seen out in top lane while at 0 CS (further than the camp
        // assignment radius from every centroid), then already clearing.
        var frames = new List<MatchTimelineFrameDto>
        {
            Frame(0, Jungler(1, x: 1200, y: 12800, jungleCs: 0)),
            Frame(60_000, JunglerAt(1, JungleCamp.BlueRaptors, jungleCs: 9)),
        };

        var clear = JungleClearBuilder.Build(MatchId, new MatchTimelineDto { Frames = frames }).Single();

        clear.StartCamp.Should().BeNull();
    }

    [Fact]
    public void Build_FullClearTime_IsNullForAnInterruptedClear()
    {
        // Invaded and killed: stalls at one camp, never reaching all six.
        var frames = new List<MatchTimelineFrameDto>
        {
            Frame(60_000, JunglerAt(1, JungleCamp.RedGromp, jungleCs: 0)),
            Frame(120_000, JunglerAt(1, JungleCamp.RedBlueBuff, jungleCs: 4)),
            Frame(180_000, JunglerAt(1, JungleCamp.RedBlueBuff, jungleCs: 7)),
        };

        var clear = JungleClearBuilder.Build(MatchId, new MatchTimelineDto { Frames = frames }).Single();

        clear.FullClearTimeMs.Should().BeNull();
        clear.StartCamp.Should().Be(nameof(JungleCamp.RedGromp));
    }

    [Fact]
    public void Build_FullClearTime_TakesTheFirstCrossingNotTheLast()
    {
        var frames = new List<MatchTimelineFrameDto>
        {
            Frame(60_000, JunglerAt(1, JungleCamp.BlueGromp, jungleCs: 0)),
            Frame(120_000, JunglerAt(1, JungleCamp.BlueWolves, jungleCs: JungleCamps.FullClearJungleCs)),
            Frame(180_000, JunglerAt(1, JungleCamp.BlueKrugs, jungleCs: 31)),
        };

        var clear = JungleClearBuilder.Build(MatchId, new MatchTimelineDto { Frames = frames }).Single();

        clear.FullClearTimeMs.Should().Be(120_000);
    }

    [Fact]
    public void Build_IgnoresNonJunglers_WithoutEnoughJungleCs()
    {
        // A laner who pokes a single camp (jungle CS grows by only 1).
        var frames = new List<MatchTimelineFrameDto>
        {
            Frame(0, JunglerAt(2, JungleCamp.BlueGromp, jungleCs: 0)),
            Frame(60_000, JunglerAt(2, JungleCamp.BlueGromp, jungleCs: 1)),
        };

        JungleClearBuilder.Build(MatchId, new MatchTimelineDto { Frames = frames }).Should().BeEmpty();
    }

    [Fact]
    public void Build_CoversBothJunglers_Independently()
    {
        var frames = new List<MatchTimelineFrameDto>
        {
            Frame(60_000,
                JunglerAt(1, JungleCamp.BlueRedBuff, jungleCs: 0),
                JunglerAt(7, JungleCamp.RedBlueBuff, jungleCs: 0)),
            Frame(120_000,
                JunglerAt(1, JungleCamp.BlueKrugs, jungleCs: 12),
                JunglerAt(7, JungleCamp.RedWolves, jungleCs: 8)),
            Frame(180_000,
                JunglerAt(1, JungleCamp.BlueWolves, jungleCs: 24),
                JunglerAt(7, JungleCamp.RedRaptors, jungleCs: 20)),
        };

        var clears = JungleClearBuilder.Build(MatchId, new MatchTimelineDto { Frames = frames });

        clears.Should().HaveCount(2);
        clears[0].StartCamp.Should().Be(nameof(JungleCamp.BlueRedBuff));
        clears[0].FullClearTimeMs.Should().Be(180_000);
        clears[1].StartCamp.Should().Be(nameof(JungleCamp.RedBlueBuff));
        clears[1].FullClearTimeMs.Should().BeNull(); // five camps at 3:00 — one short
    }

    [Fact]
    public void Build_DropsFramesPastTheFirstClearWindow()
    {
        // A mid-game frame must not contribute a sample: those rotations are what
        // made the old 8-minute window report impossible clears.
        var frames = new List<MatchTimelineFrameDto>
        {
            Frame(60_000, JunglerAt(1, JungleCamp.BlueGromp, jungleCs: 0)),
            Frame(120_000, JunglerAt(1, JungleCamp.BlueWolves, jungleCs: 12)),
            Frame(7 * 60_000, JunglerAt(1, JungleCamp.BlueKrugs, jungleCs: 44)),
        };

        var clear = JungleClearBuilder.Build(MatchId, new MatchTimelineDto { Frames = frames }).Single();

        clear.Samples.Should().HaveCount(2);
        clear.Samples.Last().TimestampMs.Should().Be(120_000);
    }

    [Fact]
    public void Build_SkipsFramesWithoutAPosition()
    {
        var frames = new List<MatchTimelineFrameDto>
        {
            Frame(60_000, JunglerAt(1, JungleCamp.BlueGromp, jungleCs: 0)),
            Frame(120_000, new MatchParticipantFrameDto
            {
                ParticipantId = 1, JungleMinionsKilled = 12, X = null, Y = null,
            }),
            Frame(180_000, JunglerAt(1, JungleCamp.BlueWolves, jungleCs: 24)),
        };

        var clear = JungleClearBuilder.Build(MatchId, new MatchTimelineDto { Frames = frames }).Single();

        clear.Samples.Select(s => s.TimestampMs).Should().Equal(60_000, 180_000);
    }

    [Fact]
    public void Build_ReturnsNothingForAnEmptyTimeline()
    {
        JungleClearBuilder.Build(MatchId, new MatchTimelineDto()).Should().BeEmpty();
    }

    private static MatchTimelineFrameDto Frame(int timestampMs, params MatchParticipantFrameDto[] participantFrames)
        => new() { TimestampMs = timestampMs, ParticipantFrames = participantFrames.ToList() };

    private static MatchParticipantFrameDto JunglerAt(int participantId, JungleCamp camp, int jungleCs)
    {
        var (x, y) = JungleCamps.Coordinates[camp];
        return Jungler(participantId, x, y, jungleCs);
    }

    private static MatchParticipantFrameDto Jungler(int participantId, int x, int y, int jungleCs)
        => new()
        {
            ParticipantId = participantId,
            JungleMinionsKilled = jungleCs,
            X = x,
            Y = y,
        };
}
