using AwesomeAssertions;
using Ingestor.Options;
using Ingestor.Processes.Components.Intake;

namespace TrueMain.UnitTests;

/// <summary>
/// The arithmetic that sizes the intake to the claim (#1361). Pinned with the production
/// numbers measured on 2026-09-02, so a future change to the defaults has to say so here.
/// </summary>
public sealed class IntakeCapacityTests
{
    [Fact]
    public void NewCandidateSlotsPerCycle_IsTheBatchLeftOverByTheEstablishedMainShare()
    {
        var matchIngestion = new MatchIngestionOptions { BatchSize = 75, EstablishedMainShare = 0.7 };

        // The complement of what the claim reserves for established mains, ceil(75 x 0.7) = 53
        // — the ~22 new accounts per cycle measured in production on 2026-09-02.
        IntakeCapacity.NewCandidateSlotsPerCycle(matchIngestion).Should().Be(22);
    }

    [Fact]
    public void NewCandidateSlotsPerCycle_StaysAtLeastOne_WhenEveryProductionSlotIsReserved()
    {
        // A share of 1 is a floor, not a partition (#900): new candidates still take whatever
        // established mains leave, so reporting a capacity of 0 would stall every derivation
        // below on a configuration that does not actually stall the claim.
        var matchIngestion = new MatchIngestionOptions { BatchSize = 75, EstablishedMainShare = 1 };

        IntakeCapacity.NewCandidateSlotsPerCycle(matchIngestion).Should().Be(1);
    }

    [Fact]
    public void PromotionCap_SplitsTheHeadroomAcrossPlatforms()
    {
        var matchIngestion = new MatchIngestionOptions { BatchSize = 750, EstablishedMainShare = 0.7 };
        var intake = new IntakeOptions { PromotionHeadroomFactor = 3, MinPromotionPerPlatform = 25 };

        // 225 new slots per cycle x 3 cycles of headroom / 3 platforms.
        IntakeCapacity.PromotionCapPerPlatform(matchIngestion, intake, platformCount: 3, topNPerPlatform: 300)
            .Should().Be(225);
    }

    [Fact]
    public void PromotionCap_NeverExceedsTopNPerPlatform()
    {
        var matchIngestion = new MatchIngestionOptions { BatchSize = 750, EstablishedMainShare = 0.7 };
        var intake = new IntakeOptions { PromotionHeadroomFactor = 3, MinPromotionPerPlatform = 25 };

        // Scoring:TopNPerPlatform stays the explicit ceiling; the derived cap only lowers it.
        IntakeCapacity.PromotionCapPerPlatform(matchIngestion, intake, platformCount: 3, topNPerPlatform: 50)
            .Should().Be(50);
    }

    [Fact]
    public void PromotionCap_IsFlooredSoASmallClaimCannotStallTheFunnel()
    {
        var matchIngestion = new MatchIngestionOptions { BatchSize = 75, EstablishedMainShare = 0.7 };
        var intake = new IntakeOptions { PromotionHeadroomFactor = 3, MinPromotionPerPlatform = 25 };

        // 22 x 3 / 3 = 22, below the floor.
        IntakeCapacity.PromotionCapPerPlatform(matchIngestion, intake, platformCount: 3, topNPerPlatform: 300)
            .Should().Be(25);
    }

    [Fact]
    public void PromotionCap_TreatsAnEmptyPlatformScopeAsOne()
    {
        var matchIngestion = new MatchIngestionOptions { BatchSize = 750, EstablishedMainShare = 0.7 };
        var intake = new IntakeOptions { PromotionHeadroomFactor = 3, MinPromotionPerPlatform = 25 };

        // Division by the platform count must not throw before the configuration validator
        // has had a chance to complain about an empty scope.
        IntakeCapacity.PromotionCapPerPlatform(matchIngestion, intake, platformCount: 0, topNPerPlatform: 3000)
            .Should().Be(675);
    }

    [Fact]
    public void RefreshBudget_IsTheClaimCapacityTimesTheHeadroom()
    {
        var matchIngestion = new MatchIngestionOptions { BatchSize = 75, EstablishedMainShare = 0.7 };
        var intake = new IntakeOptions { PromotionHeadroomFactor = 3 };

        // 22 x 3 = 66, against the 7 500 the harvest used to spend refreshing scores that
        // nobody would read before they went stale.
        IntakeCapacity.RefreshBudgetPerRun(matchIngestion, intake).Should().Be(66);
    }

    [Fact]
    public void AdaptiveShare_ReturnsTheConfiguredValueAtHalfDeficit()
    {
        IntakeCapacity.AdaptiveEstablishedMainShare(0.7, 0.2, 0.5).Should().BeApproximately(0.7, 1e-9);
    }

    [Fact]
    public void AdaptiveShare_TiltsToEstablishedMains_WhenCoverageIsMet()
    {
        // Depth over breadth (#900): nothing left to cover, so the batch buys more games from
        // the mains already tracked.
        IntakeCapacity.AdaptiveEstablishedMainShare(0.7, 0.2, 0).Should().BeApproximately(0.9, 1e-9);
    }

    [Fact]
    public void AdaptiveShare_TiltsToNewCandidates_WhenCoverageIsFarBelowTarget()
    {
        IntakeCapacity.AdaptiveEstablishedMainShare(0.7, 0.2, 1).Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void AdaptiveShare_StaysWithinTheRangeTheClaimAccepts()
    {
        IntakeCapacity.AdaptiveEstablishedMainShare(0.95, 0.4, 0).Should().Be(1);
        IntakeCapacity.AdaptiveEstablishedMainShare(0.1, 0.4, 1).Should().Be(0);
    }

    [Fact]
    public void AdaptiveShare_IsFixed_WhenTheSwingIsZero()
    {
        IntakeCapacity.AdaptiveEstablishedMainShare(0.7, 0, 0).Should().Be(0.7);
        IntakeCapacity.AdaptiveEstablishedMainShare(0.7, 0, 1).Should().Be(0.7);
    }
}
