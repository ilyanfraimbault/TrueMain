using AwesomeAssertions;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Riot.Dto;

namespace TrueMain.UnitTests;

/// <summary>
/// Covers the bounded kill-participation extraction (issue #536): one row per
/// killer + assist, only for CHAMPION_KILL events with a position before the
/// early-game cutoff.
/// </summary>
public sealed class KillPositionBuilderTests
{
    private const string MatchId = "EUW1_1";

    [Fact]
    public void Build_EmitsRowPerParticipation_BoundedToEarlyKillsWithPosition()
    {
        var timeline = new MatchTimelineDto
        {
            Events =
            [
                // Early kill with assists: killer + 2 assists = 3 rows.
                Kill(300_000, killerId: 2, assists: [3, 5], x: 1000, y: 2000),
                // Early kill, no assists: 1 row.
                Kill(800_000, killerId: 2, assists: null, x: 4000, y: 5000),
                // After the cutoff: excluded.
                Kill(950_000, killerId: 2, assists: [3], x: 9000, y: 9000),
                // No position: excluded.
                new MatchTimelineEventDto { Type = "CHAMPION_KILL", TimestampMs = 100_000, KillerId = 2 },
                // Not a kill: ignored.
                new MatchTimelineEventDto { Type = "WARD_PLACED", TimestampMs = 100_000, CreatorId = 2, PositionX = 1, PositionY = 1 },
            ]
        };

        var positions = KillPositionBuilder.Build(MatchId, timeline);

        positions.Should().HaveCount(4); // 3 (first kill) + 1 (second kill)
        positions.Should().OnlyContain(p => p.MatchId == MatchId);
        positions.Count(p => p.ParticipantId == 2).Should().Be(2);
        positions.Count(p => p.ParticipantId == 3).Should().Be(1);
        positions.Count(p => p.ParticipantId == 5).Should().Be(1);

        var assist = positions.Single(p => p.ParticipantId == 5);
        assist.TimestampMs.Should().Be(300_000);
        assist.X.Should().Be(1000);
        assist.Y.Should().Be(2000);

        positions.Should().NotContain(p => p.X == 9000, "the kill after the cutoff is excluded");
    }

    [Fact]
    public void Build_ReturnsEmpty_WhenNoEvents()
        => KillPositionBuilder.Build(MatchId, new MatchTimelineDto()).Should().BeEmpty();

    private static MatchTimelineEventDto Kill(int timestampMs, int killerId, int[]? assists, int x, int y)
        => new()
        {
            Type = "CHAMPION_KILL",
            TimestampMs = timestampMs,
            KillerId = killerId,
            AssistingParticipantIds = assists ?? [],
            PositionX = x,
            PositionY = y
        };
}
