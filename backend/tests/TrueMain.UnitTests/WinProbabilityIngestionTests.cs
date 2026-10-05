using AwesomeAssertions;
using Data.Entities;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Riot.Dto;

namespace TrueMain.UnitTests;

/// <summary>
/// The ingest side of #1911: what the timeline pass hands the win-probability builder, and the
/// row it stages from the curve.
/// </summary>
public sealed class WinProbabilityIngestionTests
{
    private static readonly string[] Positions = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];

    [Fact]
    public void ToTimeline_ReadsCreepScoreAsMinionsPlusJungle_AndSumsBountyAndShutdown()
    {
        var input = WinProbabilityIngestion.ToTimeline(1_800, Participants(), Timeline());

        input.DurationMs.Should().Be(1_800_000);
        input.Participants.Should().HaveCount(10);
        input.Participants[5].Should().Be(new Core.Lol.WinProbability.WinProbabilityTimelineParticipant(6, 200, "TOP"));
        var player = input.Frames[10].Players.Single(p => p.ParticipantId == 2);
        player.Cs.Should().Be((10 * 8) + 40);
        player.Level.Should().Be(6);

        // Only the three event types the model reads survive the reduction.
        input.Events.Select(e => e.Type).Should().Equal("CHAMPION_KILL", "ELITE_MONSTER_KILL", "CHAMPION_KILL");
        input.Events[0].Bounty.Should().Be(450);
        input.Events[0].AssistIds.Should().Equal(2);
        input.Events[1].KillerTeamId.Should().Be(200);
        input.Events[1].MonsterType.Should().Be("RIFTHERALD");
        input.Events[2].Bounty.Should().BeNull("the timeline carried neither bounty");
    }

    [Fact]
    public void Build_StagesTheCurveForTheMatch()
    {
        var row = WinProbabilityIngestion.Build("KR_1", 1_800, Participants(), Timeline());

        row.Should().NotBeNull();
        row!.MatchId.Should().Be("KR_1");
        row.Points.Should().HaveCount(31);
        row.Points[^1].Ms.Should().Be(1_800_000);
        row.Swings.Select(swing => swing.Kind).Should().OnlyContain(kind => kind == "kill");
        row.Swings[0].Bounty.Should().Be(450);
        row.Objectives.Should().ContainSingle().Which.Delta.Should().BeNull();
    }

    [Fact]
    public void Build_StagesNothing_WhenTheGameGetsNoCurve()
    {
        WinProbabilityIngestion.Build("KR_1", 840, Participants(), Timeline()).Should().BeNull();
        WinProbabilityIngestion.Build("KR_1", 1_800, Participants().Take(9).ToList(), Timeline()).Should().BeNull();
    }

    private static List<MatchParticipant> Participants()
        => Enumerable.Range(1, 10)
            .Select(participantId => new MatchParticipant
            {
                MatchId = "KR_1",
                ParticipantId = participantId,
                TeamId = participantId <= 5 ? 100 : 200,
                TeamPosition = Positions[(participantId - 1) % 5],
            })
            .ToList();

    private static MatchTimelineDto Timeline()
        => new()
        {
            Frames = Enumerable.Range(0, 31)
                .Select(minute => new MatchTimelineFrameDto
                {
                    TimestampMs = minute * 60_000,
                    ParticipantFrames = Enumerable.Range(1, 10)
                        .Select(participantId => new MatchParticipantFrameDto
                        {
                            ParticipantId = participantId,
                            MinionsKilled = minute * 8,
                            JungleMinionsKilled = participantId == 2 ? minute * 4 : 0,
                            Level = 1 + (minute / 2),
                        })
                        .ToList(),
                })
                .ToList(),
            Events =
            [
                new MatchTimelineEventDto
                {
                    Type = "CHAMPION_KILL", TimestampMs = 200_000, KillerId = 1, VictimId = 6,
                    AssistingParticipantIds = [2], Bounty = 300, ShutdownBounty = 150,
                },
                new MatchTimelineEventDto { Type = "WARD_PLACED", TimestampMs = 210_000, CreatorId = 3 },
                new MatchTimelineEventDto
                {
                    Type = "ELITE_MONSTER_KILL", TimestampMs = 840_000, KillerId = 7, KillerTeamId = 200,
                    MonsterType = "RIFTHERALD",
                },
                new MatchTimelineEventDto { Type = "CHAMPION_KILL", TimestampMs = 900_000, KillerId = 8, VictimId = 3 },
            ],
        };
}
