using AwesomeAssertions;
using Core.Lol.Ranking;

namespace TrueMain.UnitTests;

public sealed class EloBracketResolverTests
{
    private static readonly DateTime GameStart = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FromNearestSnapshot_no_snapshots_is_unranked()
    {
        EloBracketResolver.FromNearestSnapshot([], GameStart).Should().Be(EloBracket.Unranked);
    }

    [Fact]
    public void FromNearestSnapshot_picks_the_capture_closest_to_the_game()
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
    public void FromNearestSnapshot_maps_apex_tiers_to_master_plus()
    {
        (DateTime, string?)[] snapshots = [(GameStart, "GRANDMASTER")];
        EloBracketResolver.FromNearestSnapshot(snapshots, GameStart).Should().Be(EloBracket.MasterPlus);
    }

    [Fact]
    public void FromNearestSnapshot_breaks_ties_toward_the_earlier_capture()
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
