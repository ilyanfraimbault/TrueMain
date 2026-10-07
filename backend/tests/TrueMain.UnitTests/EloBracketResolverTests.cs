using AwesomeAssertions;
using Core.Lol.Ranking;

namespace TrueMain.UnitTests;

public sealed class EloBracketResolverTests
{
    private static readonly DateTime GameStart = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FromNearestSnapshot_NoSnapshotsIsUnranked()
    {
        EloBracketResolver.FromNearestSnapshot([], GameStart).Should().Be(EloBracket.Unranked);
    }

    [Fact]
    public void FromNearestSnapshot_PicksTheCaptureClosestToTheGame()
    {
        (DateTime, string?)[] snapshots =
        [
            (GameStart.AddDays(-10), "SILVER"),
            (GameStart.AddHours(-2), "GOLD"),   // closest
            (GameStart.AddDays(30), "PLATINUM"),
        ];

        EloBracketResolver.FromNearestSnapshot(snapshots, GameStart).Should().Be(EloBracket.Gold);
    }

    [Fact]
    public void FromNearestSnapshot_MapsApexTiersToTheirOwnBucket()
    {
        (DateTime, string?)[] snapshots = [(GameStart, "GRANDMASTER")];
        EloBracketResolver.FromNearestSnapshot(snapshots, GameStart).Should().Be(EloBracket.Grandmaster);
    }

    [Fact]
    public void FromNearestSnapshot_BreaksTiesTowardTheEarlierCapture()
    {
        // Two captures equidistant from the game start; the earlier one wins so the
        // bucket is deterministic across re-runs.
        (DateTime, string?)[] snapshots =
        [
            (GameStart.AddHours(1), "DIAMOND"),
            (GameStart.AddHours(-1), "GOLD"),
        ];

        EloBracketResolver.FromNearestSnapshot(snapshots, GameStart).Should().Be(EloBracket.Gold);
    }
}
