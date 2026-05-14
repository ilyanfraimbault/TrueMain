using Data.Entities;
using FluentAssertions;
using Ingestor.Processes.Components.PatternAggregation;

namespace TrueMain.UnitTests;

public sealed class SkillOrderBuilderTests
{
    [Fact]
    public void Build_reflects_the_order_basic_spells_reach_their_second_point()
    {
        var key = SkillOrderBuilder.Build(
        [
            new SkillEvent { TimestampMs = 1_000, SkillSlot = 1, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 2_000, SkillSlot = 1, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 3_000, SkillSlot = 3, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 4_000, SkillSlot = 2, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 5_000, SkillSlot = 1, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 6_000, SkillSlot = 1, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 7_000, SkillSlot = 4, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 8_000, SkillSlot = 1, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 9_000, SkillSlot = 2, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 10_000, SkillSlot = 3, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 11_000, SkillSlot = 2, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 12_000, SkillSlot = 2, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 13_000, SkillSlot = 4, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 14_000, SkillSlot = 2, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 15_000, SkillSlot = 3, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 16_000, SkillSlot = 3, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 17_000, SkillSlot = 4, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 18_000, SkillSlot = 3, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 19_000, SkillSlot = 1, LevelUpType = "EVOLVE" }
        ]);

        key.Should().Be("Q-W-E");
    }

    [Fact]
    public void Build_falls_back_to_remaining_spell_when_only_two_spells_reached_second_point()
    {
        var key = SkillOrderBuilder.Build(
        [
            new SkillEvent { TimestampMs = 1_000, SkillSlot = 1, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 2_000, SkillSlot = 1, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 3_000, SkillSlot = 2, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 4_000, SkillSlot = 3, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 5_000, SkillSlot = 1, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 6_000, SkillSlot = 4, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 7_000, SkillSlot = 2, LevelUpType = "NORMAL" }
        ]);

        key.Should().Be("Q-W-E");
    }

    [Fact]
    public void Build_returns_empty_when_there_are_no_normal_basic_skill_events()
    {
        var key = SkillOrderBuilder.Build(
        [
            new SkillEvent { TimestampMs = 1_000, SkillSlot = 4, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 2_000, SkillSlot = 1, LevelUpType = "EVOLVE" },
            new SkillEvent { TimestampMs = 3_000, SkillSlot = 2, LevelUpType = "EVOLVE" }
        ]);

        key.Should().BeEmpty();
    }
}
