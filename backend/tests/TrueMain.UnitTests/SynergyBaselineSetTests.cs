using AwesomeAssertions;
using Core.Lol.Synergy;
using Data.Entities;
using TrueMain.Services.Champions.Synergies;

namespace TrueMain.UnitTests;

/// <summary>
/// Pins the scope model the champion-synergies read (#922) scores against, with no
/// database: which side feeds the cohort, how a missing marginal reads, the
/// lane-reality check, and that the expected win rate is anchored on this scope's
/// cohort rather than on a constant.
/// </summary>
public sealed class SynergyBaselineSetTests
{
    private const string Self = SynergyBaselineSide.Self;
    private const string Ally = SynergyBaselineSide.Ally;

    [Fact]
    public void Cohort_SumsOnlyTheSelfSide()
    {
        // ALLY rows count each tracked game four times (one per teammate); folding
        // them into the cohort would double-count.
        var set = SynergyBaselineSet.From(
        [
            new(Self, 1, "TOP", 100, 60),
            new(Self, 2, "MIDDLE", 300, 150),
            new(Ally, 3, "JUNGLE", 1000, 400),
        ]);

        set.CohortGames.Should().Be(400);
        set.CohortWins.Should().Be(210);
        set.CohortWinRate.Should().BeApproximately(0.525, 1e-9);
    }

    [Fact]
    public void Lookups_AreKeyedBySideChampionAndLane_AndDefaultToEmpty()
    {
        var set = SynergyBaselineSet.From(
        [
            new(Self, 1, "TOP", 100, 60),
            new(Ally, 1, "TOP", 40, 18),
        ]);

        set.Self(1, "TOP").Should().Be(new SynergyMarginal(100, 60));
        set.Ally(1, "TOP").Should().Be(new SynergyMarginal(40, 18));
        set.Self(1, "MIDDLE").Should().Be(SynergyMarginal.Empty);
        set.Ally(2, "TOP").Should().Be(SynergyMarginal.Empty);
        SynergyMarginal.Empty.WinRate.Should().Be(0);
    }

    [Fact]
    public void UnknownSide_IsIgnored()
    {
        var set = SynergyBaselineSet.From([new("OTHER", 1, "TOP", 100, 60)]);

        set.CohortGames.Should().Be(0);
        set.Self(1, "TOP").Should().Be(SynergyMarginal.Empty);
        set.Ally(1, "TOP").Should().Be(SynergyMarginal.Empty);
    }

    [Fact]
    public void EmptyScope_HasAZeroCohort()
    {
        var set = SynergyBaselineSet.From([]);

        set.CohortGames.Should().Be(0);
        set.CohortWinRate.Should().Be(0);
    }

    [Fact]
    public void IsRealLane_MeasuresTheLaneShareOfEveryAllyGame()
    {
        // 90 jungle + 10 top ally games: jungle is 90% of the champion's games as a
        // teammate, top 10%. SELF rows never enter the denominator.
        var set = SynergyBaselineSet.From(
        [
            new(Ally, 77, "JUNGLE", 90, 45),
            new(Ally, 77, "TOP", 10, 5),
            new(Self, 77, "TOP", 5000, 2600),
        ]);

        set.IsRealLane(77, "JUNGLE", 0.5).Should().BeTrue();
        set.IsRealLane(77, "TOP", 0.1).Should().BeTrue();
        set.IsRealLane(77, "TOP", 0.11).Should().BeFalse();
    }

    [Fact]
    public void IsRealLane_FailsALaneOrChampionWithNoAllyGames()
    {
        var set = SynergyBaselineSet.From([new(Ally, 77, "JUNGLE", 90, 45)]);

        set.IsRealLane(77, "BOTTOM", 0.01).Should().BeFalse();
        set.IsRealLane(99, "TOP", 0.01).Should().BeFalse();
    }

    [Fact]
    public void IsRealLane_PassesEverythingWhenTheFloorIsDisabled()
    {
        var set = SynergyBaselineSet.From([]);

        set.IsRealLane(99, "TOP", 0d).Should().BeTrue();
        set.IsRealLane(99, "TOP", -1d).Should().BeTrue();
    }

    [Fact]
    public void ExpectedWinRate_IsAnchoredOnTheScopesCohort()
    {
        // Cohort 54%: an ally at exactly that rate is neutral here, though it
        // would read as a boost against a 50% anchor.
        var set = SynergyBaselineSet.From([new(Self, 1, "TOP", 1000, 540)]);

        set.ExpectedWinRate(0.57, [0.54]).Should().BeApproximately(0.57, 1e-9);
        set.ExpectedWinRate(0.57, [0.56, 0.52])
            .Should().BeApproximately(SynergyMath.ExpectedWinRate(0.57, [0.56, 0.52], 0.54), 1e-12);
        SynergyMath.ExpectedWinRate(0.57, [0.54], 0.5).Should().BeGreaterThan(0.57);
    }
}
