using AwesomeAssertions;
using Core.Lol.Map;

namespace TrueMain.UnitTests;

/// <summary>
/// Locks the jungle camp coordinate table and the (x, y) → nearest camp helper
/// (issue #535). Reference points are each camp's own centroid (must map to itself)
/// plus off-camp positions that must resolve to <see cref="JungleCamp.Unknown"/>.
/// </summary>
public sealed class JungleCampsTests
{
    [Fact]
    public void NearestCamp_MapsEachCampCentroidToItself()
    {
        foreach (var (camp, coordinate) in JungleCamps.Coordinates)
        {
            JungleCamps.NearestCamp(coordinate.X, coordinate.Y).Should().Be(camp);
        }
    }

    [Theory]
    [InlineData(JungleCamp.BlueGromp)]
    [InlineData(JungleCamp.BlueBlueBuff)]
    [InlineData(JungleCamp.BlueRedBuff)]
    [InlineData(JungleCamp.RedKrugs)]
    public void NearestCamp_MapsPositionNearCentroidToThatCamp(JungleCamp camp)
    {
        var (x, y) = JungleCamps.Coordinates[camp];

        // A small jitter around the centroid still resolves to the same camp.
        JungleCamps.NearestCamp(x + 250, y - 250).Should().Be(camp);
    }

    [Fact]
    public void NearestCamp_ReturnsUnknown_WhenFarFromEveryCamp()
    {
        // Deep in a base / out on a lane edge — nowhere near a jungle camp.
        JungleCamps.NearestCamp(500, 500).Should().Be(JungleCamp.Unknown);
        JungleCamps.NearestCamp(14000, 14500).Should().Be(JungleCamp.Unknown);
    }

    [Fact]
    public void NearestCamp_DistinguishesBlueAndRedSideMirrorCamps()
    {
        var blue = JungleCamps.Coordinates[JungleCamp.BlueRaptors];
        var red = JungleCamps.Coordinates[JungleCamp.RedRaptors];

        JungleCamps.NearestCamp(blue.X, blue.Y).Should().Be(JungleCamp.BlueRaptors);
        JungleCamps.NearestCamp(red.X, red.Y).Should().Be(JungleCamp.RedRaptors);
    }

    [Fact]
    public void FirstClearCampSets_AreTheSixNonScuttleCampsPerSide_AndAlignWithLolMapSides()
    {
        JungleCamps.BlueSideCamps.Should().HaveCount(6);
        JungleCamps.RedSideCamps.Should().HaveCount(6);

        // Every blue first-clear camp sits on the blue half of the map per LolMap, and
        // every red one on the red half — the side label is consistent with geometry.
        foreach (var camp in JungleCamps.BlueSideCamps)
        {
            var (x, y) = JungleCamps.Coordinates[camp];
            LolMap.IsBlueSide(x, y).Should().BeTrue();
        }

        foreach (var camp in JungleCamps.RedSideCamps)
        {
            var (x, y) = JungleCamps.Coordinates[camp];
            LolMap.IsBlueSide(x, y).Should().BeFalse();
        }
    }

    [Theory]
    [InlineData(JungleCamp.BlueGromp, true)]
    [InlineData(JungleCamp.RedKrugs, true)]
    [InlineData(JungleCamp.ScuttleTop, false)]
    [InlineData(JungleCamp.ScuttleBottom, false)]
    [InlineData(JungleCamp.Unknown, false)]
    public void IsFirstClearCamp_ExcludesScuttleAndUnknown(JungleCamp camp, bool expected)
        => JungleCamps.IsFirstClearCamp(camp).Should().Be(expected);
}
