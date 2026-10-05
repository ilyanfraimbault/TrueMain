using System.Text.Json;
using AwesomeAssertions;
using Ingestor.Riot;
using Ingestor.Riot.Dto;

namespace TrueMain.UnitTests;

/// <summary>
/// Locks the timeline mapping (issue #538): per-frame participant state
/// (position, gold, CS, jungle, damage to champions), event positions and kill
/// participants must survive into the internal <see cref="MatchTimelineDto"/> so
/// downstream analytics (#525 leads, #535 jungle pathing) can consume them.
/// </summary>
public sealed class RiotTimelineMapperTests
{
    [Fact]
    public void Map_CapturesParticipantFrames_OrderedByParticipantId()
    {
        var timeline = new RiotTimelineDto
        {
            Info = new RiotTimelineInfoDto
            {
                Frames =
                [
                    new RiotTimelineFrameDto
                    {
                        Timestamp = 60_000,
                        ParticipantFrames = new Dictionary<string, RiotTimelineParticipantFrameDto>
                        {
                            ["2"] = new()
                            {
                                ParticipantId = 2,
                                Position = new RiotTimelinePositionDto { X = 100, Y = 200 },
                                CurrentGold = 300,
                                TotalGold = 500,
                                Level = 3,
                                Xp = 1200,
                                MinionsKilled = 20,
                                JungleMinionsKilled = 4,
                                DamageStats = new RiotTimelineDamageStatsDto { TotalDamageDoneToChampions = 800 }
                            },
                            ["1"] = new() { ParticipantId = 1 }
                        }
                    }
                ]
            }
        };

        var result = RiotTimelineMapper.Map(timeline);

        result.Frames.Should().HaveCount(1);
        var frame = result.Frames[0];
        frame.TimestampMs.Should().Be(60_000);
        frame.ParticipantFrames.Select(p => p.ParticipantId).Should().Equal(1, 2);

        var second = frame.ParticipantFrames[1];
        second.X.Should().Be(100);
        second.Y.Should().Be(200);
        second.CurrentGold.Should().Be(300);
        second.TotalGold.Should().Be(500);
        second.Level.Should().Be(3);
        second.Xp.Should().Be(1200);
        second.MinionsKilled.Should().Be(20);
        second.JungleMinionsKilled.Should().Be(4);
        second.TotalDamageToChampions.Should().Be(800);
    }

    [Fact]
    public void Map_PreservesParticipantScopedEvents()
    {
        var timeline = SingleEventTimeline(new RiotTimelineEventDto
        {
            Type = "ITEM_PURCHASED",
            Timestamp = 30_000,
            ParticipantId = 5,
            ItemId = 1055
        });

        var result = RiotTimelineMapper.Map(timeline);

        var evt = result.Events.Should().ContainSingle().Subject;
        evt.ParticipantId.Should().Be(5);
        evt.Type.Should().Be("ITEM_PURCHASED");
        evt.TimestampMs.Should().Be(30_000);
        evt.ItemId.Should().Be(1055);
    }

    [Fact]
    public void Map_IncludesKillEvents_WithPositionAndParticipants()
    {
        var timeline = SingleEventTimeline(new RiotTimelineEventDto
        {
            Type = "CHAMPION_KILL",
            Timestamp = 65_000,
            ParticipantId = null,
            KillerId = 5,
            VictimId = 7,
            AssistingParticipantIds = [3, 4],
            Position = new RiotTimelinePositionDto { X = 1000, Y = 2000 }
        });

        var result = RiotTimelineMapper.Map(timeline);

        var evt = result.Events.Should().ContainSingle().Subject;
        evt.ParticipantId.Should().Be(0);
        evt.Type.Should().Be("CHAMPION_KILL");
        evt.KillerId.Should().Be(5);
        evt.VictimId.Should().Be(7);
        evt.AssistingParticipantIds.Should().Equal(3, 4);
        evt.PositionX.Should().Be(1000);
        evt.PositionY.Should().Be(2000);
    }

    [Fact]
    public void Map_LeavesMissingPositionsNull_AndAssistsEmpty()
    {
        var timeline = new RiotTimelineDto
        {
            Info = new RiotTimelineInfoDto
            {
                Frames =
                [
                    new RiotTimelineFrameDto
                    {
                        Timestamp = 0,
                        Events = [new RiotTimelineEventDto { Type = "WARD_PLACED", Timestamp = 10_000 }],
                        ParticipantFrames = new Dictionary<string, RiotTimelineParticipantFrameDto>
                        {
                            ["1"] = new() { ParticipantId = 1, Position = null }
                        }
                    }
                ]
            }
        };

        var result = RiotTimelineMapper.Map(timeline);

        result.Frames[0].TimestampMs.Should().Be(0);
        result.Frames[0].ParticipantFrames[0].X.Should().BeNull();
        result.Frames[0].ParticipantFrames[0].Y.Should().BeNull();

        var evt = result.Events.Should().ContainSingle().Subject;
        evt.PositionX.Should().BeNull();
        evt.PositionY.Should().BeNull();
        evt.AssistingParticipantIds.Should().BeEmpty();
    }

    [Fact]
    public void Map_DoesNotThrow_WhenRiotSendsNullCollections()
    {
        // Riot occasionally sends an explicit null for a frame's collections; System.Text.Json
        // honours it over the DTO `= new()` default. The mapper must degrade to empty rather than
        // NRE and poison the ingestion queue (reverting the match to queued forever).
        var timeline = new RiotTimelineDto
        {
            Info = new RiotTimelineInfoDto
            {
                Frames =
                [
                    new RiotTimelineFrameDto
                    {
                        Timestamp = 60_000,
                        Events = null!,
                        ParticipantFrames = null!
                    }
                ]
            }
        };

        var result = ((Func<MatchTimelineDto>)(() => RiotTimelineMapper.Map(timeline)))
            .Should().NotThrow().Subject;

        result.Frames.Should().ContainSingle();
        result.Frames[0].ParticipantFrames.Should().BeEmpty();
        result.Events.Should().BeEmpty();
    }

    [Fact]
    public void Map_CarriesObjectiveAndBountyFields_FromTheRawPayload()
    {
        // Riot's wire names, through the source-generated options the client uses (#1911).
        const string payload = """
            {"info":{"frames":[{"timestamp":0,"participantFrames":{},"events":[
              {"type":"BUILDING_KILL","timestamp":900000,"killerId":3,"teamId":200,"buildingType":"TOWER_BUILDING","laneType":"MID_LANE","towerType":"OUTER_TURRET","bounty":250},
              {"type":"ELITE_MONSTER_KILL","timestamp":1200000,"killerId":2,"killerTeamId":100,"monsterType":"DRAGON","monsterSubType":"FIRE_DRAGON"},
              {"type":"CHAMPION_KILL","timestamp":1300000,"killerId":9,"victimId":1,"bounty":300,"shutdownBounty":150}
            ]}]}}
            """;
        var timeline = JsonSerializer.Deserialize<RiotTimelineDto>(payload, RiotJson.Options)!;

        var events = RiotTimelineMapper.Map(timeline).Events;

        events.Should().HaveCount(3);
        events[0].TeamId.Should().Be(200);
        events[0].BuildingType.Should().Be("TOWER_BUILDING");
        events[0].LaneType.Should().Be("MID_LANE");
        events[0].TowerType.Should().Be("OUTER_TURRET");
        events[1].KillerTeamId.Should().Be(100);
        events[1].MonsterType.Should().Be("DRAGON");
        events[1].MonsterSubType.Should().Be("FIRE_DRAGON");
        events[1].TeamId.Should().BeNull();
        events[2].Bounty.Should().Be(300);
        events[2].ShutdownBounty.Should().Be(150);
        events[2].MonsterType.Should().BeNull();
    }

    private static RiotTimelineDto SingleEventTimeline(RiotTimelineEventDto evt)
        => new()
        {
            Info = new RiotTimelineInfoDto
            {
                Frames = [new RiotTimelineFrameDto { Events = [evt] }]
            }
        };
}
