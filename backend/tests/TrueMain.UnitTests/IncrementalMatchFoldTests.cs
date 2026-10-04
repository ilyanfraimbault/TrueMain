using AwesomeAssertions;
using Ingestor.Processes.Components.IncrementalFolds;

namespace TrueMain.UnitTests;

/// <summary>
/// The paging arithmetic the ban, matchup and synergy folds share since #1239 — the
/// exact loop each of them used to carry inline: batches of <c>batchSize</c>, the last
/// one shrunk to respect <c>maxPerRun</c> (0 = unbounded), and a stop on an empty or
/// short batch.
/// </summary>
public sealed class IncrementalMatchFoldTests
{
    [Fact]
    public async Task DrainAsync_FoldsFullBatchesThenTheShortTail_AndStopsOnTheShortBatch()
    {
        var backlog = new PendingBacklog(7);

        var progress = await IncrementalMatchFold.DrainAsync(3, 0, backlog.FoldAsync, CancellationToken.None);

        progress.Should().Be(new IncrementalFoldProgress(7, 3));
        backlog.Takes.Should().Equal(3, 3, 3);
        backlog.Remaining.Should().Be(0);
    }

    [Fact]
    public async Task DrainAsync_AsksOnceMore_WhenTheBacklogEndsOnABatchBoundary()
    {
        var backlog = new PendingBacklog(6);

        var progress = await IncrementalMatchFold.DrainAsync(3, 0, backlog.FoldAsync, CancellationToken.None);

        progress.Should().Be(new IncrementalFoldProgress(6, 2), "the empty third call is not a batch");
        backlog.Takes.Should().Equal(3, 3, 3);
    }

    [Fact]
    public async Task DrainAsync_ShrinksTheLastBatchToTheRunCap_AndLeavesTheRestPending()
    {
        var backlog = new PendingBacklog(10);

        var progress = await IncrementalMatchFold.DrainAsync(3, 5, backlog.FoldAsync, CancellationToken.None);

        progress.Should().Be(new IncrementalFoldProgress(5, 2));
        backlog.Takes.Should().Equal(3, 2);
        backlog.Remaining.Should().Be(5, "the cap is a hard ceiling; the rest waits for the next run");
    }

    [Fact]
    public async Task DrainAsync_ReportsNothing_WhenNothingIsPending()
    {
        var backlog = new PendingBacklog(0);

        var progress = await IncrementalMatchFold.DrainAsync(500, 20000, backlog.FoldAsync, CancellationToken.None);

        progress.Should().Be(new IncrementalFoldProgress(0, 0));
        backlog.Takes.Should().Equal(500);
    }

    [Fact]
    public async Task DrainAsync_StopsBeforeTheNextBatch_WhenCancelled()
    {
        using var cts = new CancellationTokenSource();
        var backlog = new PendingBacklog(10, onFold: cts.Cancel);

        var act = () => IncrementalMatchFold.DrainAsync(3, 0, backlog.FoldAsync, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        backlog.Takes.Should().Equal(3);
    }

    private sealed class PendingBacklog(int pending, Action? onFold = null)
    {
        public List<int> Takes { get; } = [];

        public int Remaining { get; private set; } = pending;

        public Task<int> FoldAsync(int take, CancellationToken ct)
        {
            Takes.Add(take);
            var folded = Math.Min(take, Remaining);
            Remaining -= folded;
            onFold?.Invoke();
            return Task.FromResult(folded);
        }
    }
}
