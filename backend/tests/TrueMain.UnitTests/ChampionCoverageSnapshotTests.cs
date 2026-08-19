using AwesomeAssertions;
using Ingestor.Processes.Components.Coverage;

namespace TrueMain.UnitTests;

public sealed class ChampionCoverageSnapshotTests
{
    private const string Kr = "KR";
    private const string Euw = "EUW1";

    [Fact]
    public void Empty_ReturnsZeroDeficit_ForAnyChampion()
    {
        ChampionCoverageSnapshot.Empty.Deficit(Kr, 266).Should().Be(0);
    }

    [Theory]
    [InlineData(0, 1.0)]   // no mains => maximal scarcity
    [InlineData(5, 0.75)]  // quarter of target
    [InlineData(10, 0.5)]  // half of target
    [InlineData(20, 0.0)]  // at target
    [InlineData(40, 0.0)]  // above target => clamped to 0
    public void Deficit_InterpolatesAndClamps(int mains, double expected)
    {
        var snapshot = Build(new() { [(Kr, 266)] = mains });

        snapshot.Deficit(Kr, 266).Should().BeApproximately(expected, 0.0001);
    }

    [Fact]
    public void Deficit_IsScopedToThePlatform()
    {
        // The regression #1150 was built on: a champion saturated on one region read as
        // covered on every region, so the under-served ones got no scarcity signal at all.
        var snapshot = Build(new() { [(Euw, 266)] = 60, [(Kr, 266)] = 1 });

        snapshot.Deficit(Euw, 266).Should().Be(0);
        snapshot.Deficit(Kr, 266).Should().BeApproximately(0.95, 0.0001);
    }

    [Fact]
    public void SaturatedChampionIdsFor_HoldsOnlyChampionsAtOrAboveTargetOnThatPlatform()
    {
        // #900: past the target, another main on the same champion is worth less than
        // more games from the mains already tracked, so those champions are the ones
        // pushed to the back of the promotion queue — per region since #1150.
        var snapshot = Build(new()
        {
            [(Euw, 1)] = 19,
            [(Euw, 2)] = 20,
            [(Euw, 3)] = 45,
            [(Kr, 3)] = 2
        });

        snapshot.SaturatedChampionIdsFor(Euw).Should().BeEquivalentTo([2, 3]);
        snapshot.SaturatedChampionIdsFor(Kr).Should().BeEmpty();
    }

    [Fact]
    public void SaturatedChampionIdsFor_IsEmpty_ForAPlatformWithNoMains()
    {
        var snapshot = Build(new() { [(Euw, 1)] = 40 });

        snapshot.SaturatedChampionIdsFor("NA1").Should().BeEmpty();
    }

    [Fact]
    public void Empty_HasNoSaturatedChampion()
    {
        // The neutral snapshot must not deprioritise anything: at cold start every
        // champion still needs its first mains.
        ChampionCoverageSnapshot.Empty.SaturatedChampionIdsFor(Kr).Should().BeEmpty();
    }

    [Fact]
    public void Deficit_IsMaximal_ForChampionMissingFromNonEmptySnapshot()
    {
        var snapshot = Build(new() { [(Kr, 1)] = 30 });

        // Champion 99 is absent => 0 mains => deficit 1 (snapshot carries data).
        snapshot.Deficit(Kr, 99).Should().Be(1);
    }

    [Fact]
    public void MeanDeficit_AveragesOverTheSharedChampionUniverse()
    {
        // Champion 2 exists only on EUW1. KR must still be charged its full deficit for it —
        // averaging over KR's own keys would score a region as perfectly covered precisely
        // because it is missing champions.
        var snapshot = Build(new()
        {
            [(Euw, 1)] = 20,
            [(Euw, 2)] = 20,
            [(Kr, 1)] = 10
        });

        snapshot.MeanDeficit(Euw).Should().Be(0);
        // KR: champion 1 at half target (0.5), champion 2 absent (1.0) => mean 0.75.
        snapshot.MeanDeficit(Kr).Should().BeApproximately(0.75, 0.0001);
    }

    [Fact]
    public void MeanDeficit_IsOne_ForAPlatformWithNoMainsAtAll()
    {
        var snapshot = Build(new() { [(Euw, 1)] = 20, [(Euw, 2)] = 20 });

        snapshot.MeanDeficit("NA1").Should().Be(1);
    }

    [Fact]
    public void MeanDeficit_IsZero_OnTheNeutralSnapshot()
    {
        // Cold start has no reason to favour a region; an even split is the right default.
        ChampionCoverageSnapshot.Empty.MeanDeficit(Kr).Should().Be(0);
    }

    [Fact]
    public void Constructor_TreatsNonPositiveTargetAsOne()
    {
        var snapshot = Build(new() { [(Kr, 1)] = 0 }, targetMainsPerChampion: 0);

        snapshot.Deficit(Kr, 1).Should().Be(1);
    }

    [Fact]
    public void Constructor_Throws_ForEmptyDictionary()
    {
        var act = () => new ChampionCoverageSnapshot(
            new Dictionary<(string, int), int>(),
            targetMainsPerChampion: 20);

        act.Should().Throw<ArgumentException>();
    }

    private static ChampionCoverageSnapshot Build(
        Dictionary<(string PlatformId, int ChampionId), int> mains,
        int targetMainsPerChampion = 20)
        => new(mains, targetMainsPerChampion);
}
