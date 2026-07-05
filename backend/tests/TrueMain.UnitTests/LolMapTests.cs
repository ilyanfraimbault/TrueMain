using AwesomeAssertions;
using Core.Lol.Map;

namespace TrueMain.UnitTests;

/// <summary>
/// Locks the Summoner's Rift zone classifier (issue #538). Reference points use
/// real turret/inhibitor coordinates so the heuristic bands stay anchored to the
/// actual map geometry.
/// </summary>
public sealed class LolMapTests
{
    [Theory]
    [InlineData(981, 10441, MapZone.TopLane)]   // blue top outer turret
    [InlineData(3203, 3208, MapZone.MidLane)]   // blue mid inhibitor (on the nx≈ny diagonal)
    [InlineData(13624, 10572, MapZone.BotLane)] // red bot base turret
    [InlineData(500, 500, MapZone.BlueBase)]
    [InlineData(14000, 14500, MapZone.RedBase)]
    [InlineData(3000, 11800, MapZone.River)]    // upper-left river, off both lanes
    [InlineData(6500, 4000, MapZone.Jungle)]    // blue-side jungle quadrant
    public void Classify_AssignsExpectedZone(int x, int y, MapZone expected)
        => LolMap.Classify(x, y).Should().Be(expected);

    [Fact]
    public void Classify_ClampsOutOfBoundsCoordinates()
    {
        LolMap.Classify(-5000, -5000).Should().Be(MapZone.BlueBase);
        LolMap.Classify(99999, 99999).Should().Be(MapZone.RedBase);
    }

    [Theory]
    [InlineData(500, 500, true)]        // blue base
    [InlineData(14000, 14500, false)]   // red base
    [InlineData(981, 10441, true)]      // blue-side top lane (below anti-diagonal)
    public void IsBlueSide_SplitsOnRiverAntiDiagonal(int x, int y, bool expected)
        => LolMap.IsBlueSide(x, y).Should().Be(expected);

    [Theory]
    // A blue-side MIDDLE laner. Roam = a different lane, the enemy (red-side)
    // jungle, or the enemy (red) base. Own lane, the river, own-side jungle and
    // own base are normal lane-phase movement and never count.
    [InlineData(3203, 3208, false)]   // own mid lane → not a roam
    [InlineData(13624, 10572, true)]  // bot lane → roam (different lane)
    [InlineData(6500, 4000, false)]   // blue-side (own) jungle → not a roam
    [InlineData(8500, 11000, true)]   // red-side (enemy) jungle → roam
    [InlineData(3000, 11800, false)]  // river → not a roam
    [InlineData(14000, 14500, true)]  // red (enemy) base → roam
    [InlineData(500, 500, false)]     // blue (own) base → not a roam
    public void IsRoam_FlagsOtherLanesAndEnemySideOnly(int x, int y, bool expected)
        => LolMap.IsRoam(x, y, MapZone.MidLane, ownIsBlueSide: true).Should().Be(expected);

    [Fact]
    public void IsRoam_IsRelativeToTeamSide()
    {
        // The same red-side jungle point is a roam for a blue laner (enemy jungle)
        // but not for a red laner (own jungle) — side flips the verdict.
        LolMap.IsRoam(8500, 11000, MapZone.MidLane, ownIsBlueSide: true).Should().BeTrue();
        LolMap.IsRoam(8500, 11000, MapZone.MidLane, ownIsBlueSide: false).Should().BeFalse();
    }
}
