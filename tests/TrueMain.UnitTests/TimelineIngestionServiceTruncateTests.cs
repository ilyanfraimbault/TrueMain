using Data.Entities;
using FluentAssertions;
using Ingestor.Processes.Components.MatchIngestion;

namespace TrueMain.UnitTests;

public sealed class TimelineIngestionServiceTruncateTests
{
    [Fact]
    public void TruncateSkillEvents_KeepsListUnchanged_WhenAtOrBelowCap()
    {
        var events = MakeEvents(TimelineIngestionService.MaxSkillEventsPerParticipant);

        var truncated = TimelineIngestionService.TruncateSkillEvents(events);

        truncated.Should().HaveCount(TimelineIngestionService.MaxSkillEventsPerParticipant);
        truncated.Should().BeSameAs(events);
    }

    [Fact]
    public void TruncateSkillEvents_KeepsEarliestEventsByTimestamp_WhenAboveCap()
    {
        var events = new List<SkillEvent>
        {
            new() { TimestampMs = 500, SkillSlot = 1, LevelUpType = "NORMAL" },
            new() { TimestampMs = 100, SkillSlot = 2, LevelUpType = "NORMAL" },
            new() { TimestampMs = 1500, SkillSlot = 3, LevelUpType = "NORMAL" },
            new() { TimestampMs = 200, SkillSlot = 1, LevelUpType = "NORMAL" },
            new() { TimestampMs = 800, SkillSlot = 2, LevelUpType = "NORMAL" },
            new() { TimestampMs = 1800, SkillSlot = 3, LevelUpType = "NORMAL" },
            new() { TimestampMs = 300, SkillSlot = 1, LevelUpType = "NORMAL" },
            new() { TimestampMs = 1000, SkillSlot = 2, LevelUpType = "NORMAL" },
            new() { TimestampMs = 600, SkillSlot = 1, LevelUpType = "NORMAL" },
            new() { TimestampMs = 1200, SkillSlot = 3, LevelUpType = "NORMAL" },
            new() { TimestampMs = 1400, SkillSlot = 1, LevelUpType = "NORMAL" },
            new() { TimestampMs = 2000, SkillSlot = 2, LevelUpType = "NORMAL" },
            new() { TimestampMs = 2200, SkillSlot = 3, LevelUpType = "NORMAL" }
        };

        var truncated = TimelineIngestionService.TruncateSkillEvents(events);

        truncated.Should().HaveCount(TimelineIngestionService.MaxSkillEventsPerParticipant);
        truncated.Select(skillEvent => skillEvent.TimestampMs).Should().BeInAscendingOrder();
        truncated.First().TimestampMs.Should().Be(100);
        truncated.Last().TimestampMs.Should().Be(1800);
        var droppedTimestamps = new[] { 2000, 2200 };
        truncated.Select(skillEvent => skillEvent.TimestampMs).Should().NotIntersectWith(droppedTimestamps);
    }

    [Fact]
    public void TruncateSkillEvents_DoesNotMutateInputList_WhenTruncating()
    {
        var events = MakeEvents(TimelineIngestionService.MaxSkillEventsPerParticipant + 5);

        var truncated = TimelineIngestionService.TruncateSkillEvents(events);

        events.Should().HaveCount(TimelineIngestionService.MaxSkillEventsPerParticipant + 5);
        truncated.Should().NotBeSameAs(events);
    }

    private static List<SkillEvent> MakeEvents(int count)
    {
        return Enumerable.Range(1, count)
            .Select(i => new SkillEvent
            {
                TimestampMs = i * 100,
                SkillSlot = (i % 3) + 1,
                LevelUpType = "NORMAL"
            })
            .ToList();
    }
}
