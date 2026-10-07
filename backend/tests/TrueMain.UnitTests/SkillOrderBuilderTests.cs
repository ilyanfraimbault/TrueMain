using Data.BuildFacts;
using Data.Entities;
using AwesomeAssertions;

namespace TrueMain.UnitTests;

public sealed class SkillOrderBuilderTests
{
    [Fact]
    public void Build_ReflectsTheOrderBasicSpellsReachTheirSecondPoint()
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
    public void Build_FallsBackToRemainingSpellWhenOnlyTwoSpellsReachedSecondPoint()
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
    public void Build_ReturnsEmptyWhenThereAreNoNormalBasicSkillEvents()
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
