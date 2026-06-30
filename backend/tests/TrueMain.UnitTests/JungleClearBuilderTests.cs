using AwesomeAssertions;
using Core.Lol.Map;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Riot.Dto;

namespace TrueMain.UnitTests;

/// <summary>
/// Covers first-clear reconstruction (issue #535): jungler identification from the
/// frames, camp order from the per-minute position trail, jungle-CS-delta gating,
/// and the full-clear stop. Frames are hand-built at camp centroids so the
/// nearest-camp mapping is unambiguous.
/// </summary>
public sealed class JungleClearBuilderTests
{
    private const string MatchId = "EUW1_1";

    // A standard blue-side clear, one camp per minute starting at minute 1.
    private static readonly JungleCamp[] BlueClearOrder =
    [
        JungleCamp.BlueGromp,
        JungleCamp.BlueBlueBuff,
        JungleCamp.BlueWolves,
        JungleCamp.BlueRaptors,
        JungleCamp.BlueRedBuff,
        JungleCamp.BlueKrugs
    ];

    [Fact]
    public void Build_ReconstructsFullBlueSideFirstClear_InOrder_WithTiming()
    {
        var frames = new List<MatchTimelineFrameDto> { Frame(0, JunglerAt(1, JungleCamp.BlueGromp, jungleCs: 0)) };

        // Minute n (n=1..6): jungler sits on camp n with cumulative jungle CS = n.
        for (var i = 0; i < BlueClearOrder.Length; i++)
        {
            var minute = i + 1;
            frames.Add(Frame(minute * 60_000, JunglerAt(1, BlueClearOrder[i], jungleCs: minute)));
        }

        var clears = JungleClearBuilder.Build(MatchId, new MatchTimelineDto { Frames = frames });

        clears.Should().HaveCount(1);
        var clear = clears[0];
        clear.MatchId.Should().Be(MatchId);
        clear.ParticipantId.Should().Be(1);
        clear.Steps.Select(s => s.Camp)
            .Should().Equal(BlueClearOrder.Select(c => c.ToString()));
        clear.Steps.Select(s => s.TimestampMs)
            .Should().Equal(Enumerable.Range(1, 6).Select(m => m * 60_000));
        clear.FullClearTimeMs.Should().Be(6 * 60_000); // last (Krugs) frame
    }

    [Fact]
    public void Build_IgnoresNonJunglers_WithoutEnoughJungleCs()
    {
        // A laner who pokes a single camp (jungle CS grows by only 1) is not a jungler.
        var frames = new List<MatchTimelineFrameDto>
        {
            Frame(0, JunglerAt(2, JungleCamp.BlueGromp, jungleCs: 0)),
            Frame(60_000, JunglerAt(2, JungleCamp.BlueGromp, jungleCs: 1))
        };

        JungleClearBuilder.Build(MatchId, new MatchTimelineDto { Frames = frames }).Should().BeEmpty();
    }

    [Fact]
    public void Build_DoesNotCreditCamp_WhenJungleCsDidNotAdvance()
    {
        // Jungler clears Gromp then Blue, but a frame on the way to Wolves has no new
        // jungle CS (pathing through) — Wolves must not be credited from that frame.
        var frames = new List<MatchTimelineFrameDto>
        {
            Frame(0, JunglerAt(1, JungleCamp.BlueGromp, jungleCs: 0)),
            Frame(60_000, JunglerAt(1, JungleCamp.BlueGromp, jungleCs: 4)),
            Frame(120_000, JunglerAt(1, JungleCamp.BlueBlueBuff, jungleCs: 8)),
            // Standing on Wolves but jungle CS unchanged -> no credit.
            Frame(180_000, JunglerAt(1, JungleCamp.BlueWolves, jungleCs: 8))
        };

        var clears = JungleClearBuilder.Build(MatchId, new MatchTimelineDto { Frames = frames });

        clears.Should().HaveCount(1);
        clears[0].Steps.Select(s => s.Camp)
            .Should().Equal(JungleCamp.BlueGromp.ToString(), JungleCamp.BlueBlueBuff.ToString());
        clears[0].FullClearTimeMs.Should().BeNull(); // never finished the six camps
    }

    [Fact]
    public void Build_SkipsGankMinutes_WhereJunglerIsNotOnACamp()
    {
        // Between Gromp and Wolves the jungler ganks mid (off any camp). The gank frame
        // maps to Unknown and is skipped; the clear order stays camps-only.
        var frames = new List<MatchTimelineFrameDto>
        {
            Frame(0, JunglerAt(1, JungleCamp.BlueGromp, jungleCs: 0)),
            Frame(60_000, JunglerAt(1, JungleCamp.BlueGromp, jungleCs: 4)),
            Frame(120_000, JunglerAtPosition(1, 7300, 7400, jungleCs: 4)), // mid lane gank, no new CS
            Frame(180_000, JunglerAt(1, JungleCamp.BlueWolves, jungleCs: 8))
        };

        var clears = JungleClearBuilder.Build(MatchId, new MatchTimelineDto { Frames = frames });

        clears[0].Steps.Select(s => s.Camp)
            .Should().Equal(JungleCamp.BlueGromp.ToString(), JungleCamp.BlueWolves.ToString());
    }

    [Fact]
    public void Build_StopsAtFullClear_IgnoringLaterCamps()
    {
        var frames = new List<MatchTimelineFrameDto> { Frame(0, JunglerAt(1, JungleCamp.BlueGromp, jungleCs: 0)) };
        for (var i = 0; i < BlueClearOrder.Length; i++)
        {
            frames.Add(Frame((i + 1) * 60_000, JunglerAt(1, BlueClearOrder[i], jungleCs: i + 1)));
        }

        // A 7th frame on the enemy Gromp after the clear is finished — must be ignored.
        frames.Add(Frame(7 * 60_000, JunglerAt(1, JungleCamp.RedGromp, jungleCs: 7)));

        var clear = JungleClearBuilder.Build(MatchId, new MatchTimelineDto { Frames = frames }).Single();

        clear.Steps.Should().HaveCount(6);
        clear.Steps.Should().NotContain(s => s.Camp == JungleCamp.RedGromp.ToString());
        clear.FullClearTimeMs.Should().Be(6 * 60_000);
    }

    [Fact]
    public void Build_HandlesBothJunglers_OnePerSide()
    {
        // Real timelines always have a t=0 frame (jungle CS 0) that primes the CS
        // baseline so the very first camp's delta is observable.
        var frames = new List<MatchTimelineFrameDto>
        {
            Frame(0,
                JunglerAt(1, JungleCamp.BlueGromp, jungleCs: 0),
                JunglerAt(6, JungleCamp.RedGromp, jungleCs: 0))
        };
        for (var i = 0; i < BlueClearOrder.Length; i++)
        {
            var minute = i + 1;
            frames.Add(Frame(
                minute * 60_000,
                JunglerAt(1, BlueClearOrder[i], jungleCs: minute),
                JunglerAt(6, MirrorRedCamp(BlueClearOrder[i]), jungleCs: minute)));
        }

        var clears = JungleClearBuilder.Build(MatchId, new MatchTimelineDto { Frames = frames });

        clears.Select(c => c.ParticipantId).Should().Equal(1, 6);
        clears.Single(c => c.ParticipantId == 1).Steps.Should().HaveCount(6);
        clears.Single(c => c.ParticipantId == 6).Steps
            .Select(s => s.Camp)
            .Should().OnlyContain(camp => JungleCamps.RedSideCamps.Select(c => c.ToString()).Contains(camp));
    }

    [Fact]
    public void Build_IgnoresFramesPastFirstClearWindow()
    {
        // Jungle activity only after the window -> no jungler detected within it.
        var frames = new List<MatchTimelineFrameDto>
        {
            Frame(0, JunglerAt(1, JungleCamp.BlueGromp, jungleCs: 0)),
            Frame(JungleClearBuilder.FirstClearWindowMs + 60_000, JunglerAt(1, JungleCamp.BlueKrugs, jungleCs: 12))
        };

        JungleClearBuilder.Build(MatchId, new MatchTimelineDto { Frames = frames }).Should().BeEmpty();
    }

    [Fact]
    public void Build_ReturnsEmpty_WhenNoFrames()
        => JungleClearBuilder.Build(MatchId, new MatchTimelineDto()).Should().BeEmpty();

    private static JungleCamp MirrorRedCamp(JungleCamp blueCamp)
        => JungleCamps.RedSideCamps[JungleCamps.BlueSideCamps.ToList().IndexOf(blueCamp)];

    private static MatchTimelineFrameDto Frame(int timestampMs, params MatchParticipantFrameDto[] participants)
        => new() { TimestampMs = timestampMs, ParticipantFrames = [.. participants] };

    private static MatchParticipantFrameDto JunglerAt(int participantId, JungleCamp camp, int jungleCs)
    {
        var (x, y) = JungleCamps.Coordinates[camp];
        return JunglerAtPosition(participantId, x, y, jungleCs);
    }

    private static MatchParticipantFrameDto JunglerAtPosition(int participantId, int x, int y, int jungleCs)
        => new()
        {
            ParticipantId = participantId,
            X = x,
            Y = y,
            JungleMinionsKilled = jungleCs
        };
}
