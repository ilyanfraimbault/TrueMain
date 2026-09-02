using AwesomeAssertions;
using Ingestor.Options;
using Ingestor.Processes.Components.Intake;

namespace TrueMain.UnitTests;

/// <summary>
/// How much of an over-deep <c>Queued</c> backlog one retention run demotes, and in statements
/// of what size (#1361).
/// </summary>
public sealed class QueueDepthDrainTests
{
    private static IntakeOptions Options(int cap = 5000, int batchSize = 5000, int maxBatches = 4)
        => new()
        {
            MaxQueuedPerPlatform = cap,
            QueueDepthDemotionBatchSize = batchSize,
            MaxDemotionBatchesPerRun = maxBatches
        };

    [Fact]
    public void PlansNothing_WhenThePlatformIsWithinItsCap()
    {
        QueueDepthDrain.PlanBatches(4999, Options()).Should().BeEmpty();
        QueueDepthDrain.PlanBatches(5000, Options()).Should().BeEmpty();
    }

    [Fact]
    public void PlansOnlyTheExcess_NotTheWholeQueue()
    {
        // The cap is a target depth, not a purge: 5 000 rows stay queued.
        QueueDepthDrain.PlanBatches(6200, Options()).Should().Equal(1200);
    }

    [Fact]
    public void SplitsTheExcessIntoBoundedStatements()
    {
        QueueDepthDrain.PlanBatches(17_000, Options(batchSize: 5000)).Should().Equal(5000, 5000, 2000);
    }

    [Fact]
    public void StopsAtTheRunBudget_SoADeepBacklogDrainsAcrossCycles()
    {
        // 258 000 rows over the cap is the production shape: one run takes 4 x 5 000 and the
        // rest waits for the next cycle rather than putting the whole backlog in one
        // transaction (#988).
        var batches = QueueDepthDrain.PlanBatches(263_000, Options(batchSize: 5000, maxBatches: 4));

        batches.Should().Equal(5000, 5000, 5000, 5000);
        batches.Sum().Should().Be(20_000);
    }

    [Fact]
    public void PlansNothing_WhenTheDrainIsDisabled()
    {
        QueueDepthDrain.PlanBatches(500_000, Options(cap: 0)).Should().BeEmpty();
        QueueDepthDrain.PlanBatches(500_000, Options(maxBatches: 0)).Should().BeEmpty();
    }

    [Fact]
    public void TreatsANonPositiveBatchSizeAsOne_RatherThanLoopingForever()
    {
        QueueDepthDrain.PlanBatches(10, Options(cap: 5, batchSize: 0, maxBatches: 3)).Should().Equal(1, 1, 1);
    }
}
