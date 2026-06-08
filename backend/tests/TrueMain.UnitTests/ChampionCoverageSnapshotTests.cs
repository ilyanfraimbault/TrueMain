using AwesomeAssertions;
using Ingestor.Processes.Components.Coverage;

namespace TrueMain.UnitTests;

public sealed class ChampionCoverageSnapshotTests
{
    [Fact]
    public void Empty_ReturnsZeroDeficit_ForAnyChampion()
    {
        ChampionCoverageSnapshot.Empty.Deficit(266).Should().Be(0);
        ChampionCoverageSnapshot.Empty.MainsFor(266).Should().Be(0);
    }

    [Theory]
    [InlineData(0, 1.0)]   // no mains => maximal scarcity
    [InlineData(5, 0.75)]  // quarter of target
    [InlineData(10, 0.5)]  // half of target
    [InlineData(20, 0.0)]  // at target
    [InlineData(40, 0.0)]  // above target => clamped to 0
    public void Deficit_InterpolatesAndClamps(int mains, double expected)
    {
        var snapshot = new ChampionCoverageSnapshot(
            new Dictionary<int, int> { [266] = mains },
            targetMainsPerChampion: 20);

        snapshot.Deficit(266).Should().BeApproximately(expected, 0.0001);
    }

    [Fact]
    public void Deficit_IsMaximal_ForChampionMissingFromNonEmptySnapshot()
    {
        var snapshot = new ChampionCoverageSnapshot(
            new Dictionary<int, int> { [1] = 30 },
            targetMainsPerChampion: 20);

        // Champion 99 is absent => 0 mains => deficit 1 (snapshot carries data).
        snapshot.Deficit(99).Should().Be(1);
    }

    [Fact]
    public void Constructor_TreatsNonPositiveTargetAsOne()
    {
        var snapshot = new ChampionCoverageSnapshot(
            new Dictionary<int, int> { [1] = 0 },
            targetMainsPerChampion: 0);

        snapshot.Deficit(1).Should().Be(1);
    }

    [Fact]
    public void Constructor_Throws_ForEmptyDictionary()
    {
        var act = () => new ChampionCoverageSnapshot(new Dictionary<int, int>(), targetMainsPerChampion: 20);

        act.Should().Throw<ArgumentException>();
    }
}
