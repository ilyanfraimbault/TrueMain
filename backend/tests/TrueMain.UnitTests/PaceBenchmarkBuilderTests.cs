using AwesomeAssertions;
using Core.Lol.Pace;
using Data.Entities;
using Data.Repositories;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Riot.Dto;

namespace TrueMain.UnitTests;

public sealed class PaceBenchmarkBuilderTests
{
    private static readonly DateTime GameStart = new(2026, 10, 1, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ResolveLobbyTier_TakesTheLowerMedianOfTheTrackedAccountsTiers()
    {
        var (diamond, emerald, master) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var participants = new[] { Participant(1, "TOP", diamond), Participant(2, "MIDDLE", emerald), Participant(3, "JUNGLE", master), Participant(4, "UTILITY") };
        var history = new Dictionary<Guid, List<(DateTime, string?)>>
        {
            [diamond] = [(GameStart.AddDays(-1), "DIAMOND")],
            [emerald] = [(GameStart.AddDays(-1), "EMERALD")],
            [master] = [(GameStart.AddDays(-1), "MASTER")],
        };

        PaceBenchmarkBuilder.ResolveLobbyTier(participants, history, GameStart).Should().Be("DIAMOND");
    }

    [Fact]
    public void ResolveLobbyTier_IsNullWhenNoTrackedAccountWasRanked()
    {
        var unranked = Guid.NewGuid();
        var participants = new[] { Participant(1, "TOP", unranked), Participant(2, "MIDDLE", Guid.NewGuid()), Participant(3, "JUNGLE") };
        var history = new Dictionary<Guid, List<(DateTime, string?)>> { [unranked] = [(GameStart, "")] };

        PaceBenchmarkBuilder.ResolveLobbyTier(participants, history, GameStart).Should().BeNull();
    }

    [Fact]
    public void Build_CountsEveryLanerAtEachMinuteTheGameReached()
    {
        var tracked = Guid.NewGuid();
        var participants = new[] { Participant(1, "MIDDLE", tracked), Participant(2, "JUNGLE"), Participant(3, "") };
        var history = new Dictionary<Guid, List<(DateTime, string?)>> { [tracked] = [(GameStart, "EMERALD")] };
        // Frames every minute up to the end frame at 6:40 — the end frame must not count as minute 7.
        var timeline = Timeline(Enumerable.Range(0, 7).Select(minute => minute * 60_000 + 15).Append(400_000));

        var keys = PaceBenchmarkBuilder.Build(Claim(400), participants, timeline, history);

        // Minutes 1..6, two laners (the third has no canonical position), two metrics.
        keys.Should().HaveCount(6 * 2 * 2);
        keys.Should().OnlyContain(key => key.Patch == "16.19" && key.Tier == "EMERALD" && key.Minute >= 1 && key.Minute <= 6);
        keys.Should().Contain(new PaceBenchmarkKey("16.19", "EMERALD", "MIDDLE", 6, PaceMetric.Cs, PaceHistogram.ToBucket(PaceMetric.Cs, 6 * 8 + 6)));
        keys.Should().Contain(new PaceBenchmarkKey("16.19", "EMERALD", "JUNGLE", 6, PaceMetric.GoldEarned, PaceHistogram.ToBucket(PaceMetric.GoldEarned, 500 + 6 * 400)));
    }

    [Fact]
    public void Build_SkipsARemake()
    {
        var tracked = Guid.NewGuid();
        var history = new Dictionary<Guid, List<(DateTime, string?)>> { [tracked] = [(GameStart, "EMERALD")] };

        PaceBenchmarkBuilder.Build(Claim(240), [Participant(1, "TOP", tracked)], Timeline([0, 60_000, 120_000, 180_000, 240_000]), history)
            .Should().BeEmpty();
    }

    [Fact]
    public void Count_SumsIdenticalKeys()
    {
        var key = new PaceBenchmarkKey("16.19", "GOLD", "TOP", 5, PaceMetric.Cs, 6);
        var other = key with { Bucket = 7 };

        PaceBenchmarkBuilder.Count([key, other, key]).Should().BeEquivalentTo(
            [new PaceBenchmarkCount(key, 2), new PaceBenchmarkCount(other, 1)]);
    }

    private static PaceBenchmarkFoldClaim Claim(int durationSeconds) => new("EUW1_1", "16.19", GameStart, durationSeconds);

    private static MatchParticipant Participant(int participantId, string position, Guid? accountId = null) => new()
    {
        MatchId = "EUW1_1",
        ParticipantId = participantId,
        TeamPosition = position,
        RiotAccountId = accountId,
    };

    /// <summary>Frames at <paramref name="timestamps"/>, each participant at 8 minions, 1 monster and 400 gold a minute.</summary>
    private static MatchTimelineDto Timeline(IEnumerable<int> timestamps) => new()
    {
        Frames = timestamps.Select(timestamp => new MatchTimelineFrameDto
        {
            TimestampMs = timestamp,
            ParticipantFrames = Enumerable.Range(1, 3).Select(id => new MatchParticipantFrameDto
            {
                ParticipantId = id,
                MinionsKilled = timestamp / 60_000 * 8,
                JungleMinionsKilled = timestamp / 60_000,
                TotalGold = 500 + timestamp / 60_000 * 400,
            }).ToList(),
        }).ToList(),
    };
}
