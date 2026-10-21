using System.Collections.Concurrent;
using AwesomeAssertions;
using Ingestor.FetchWorkers;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Processes.Summaries;
using Ingestor.Riot.RateLimiting;
using Ingestor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// The permanent match-fetch workers (#1457): one per regional routing value, each claiming
/// only its own platforms, looping until the host stops, surviving a failed batch, and
/// leaving no claim behind when a shutdown cuts a batch short.
/// </summary>
public sealed class MatchFetchWorkerPoolTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);

    [Fact]
    public void Routes_GroupsTheConfiguredPlatformsByRegionalRoutingValue()
    {
        var pool = BuildPool(new ScriptedProcess(), ["EUW1", "kr", "NA1", "EUN1"], out _, out _);

        pool.Routes().Should().BeEquivalentTo(
            [
                new MatchFetchRoute("americas", ["NA1"]),
                new MatchFetchRoute("asia", ["KR"]),
                new MatchFetchRoute("europe", ["EUW1", "EUN1"])
            ],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task RunAsync_RunOnce_RunsOneBatchPerRouteNarrowedToItsPlatforms()
    {
        var process = new ScriptedProcess();
        var pool = BuildPool(process, ["EUW1", "KR", "NA1"], out var limiter, out _, batchSize: 6);

        await pool.RunAsync(runOnce: true, CancellationToken.None).WaitAsync(Settle);

        process.Batches.Select(batch => (string.Join('+', batch.Platforms), batch.BatchSize)).Should().BeEquivalentTo(
            [("NA1", 6), ("KR", 6), ("EUW1", 6)]);
        process.Batches.Should().OnlyContain(batch => batch.Caller == "MatchIngestion" && batch.Mode == JobMode.FetchWorkers);

        // The target rate is the worker's ceiling for as long as the workers run, and only then.
        limiter.Received(1).SetCallerCeiling("MatchIngestion", 30);
        limiter.Received(1).SetCallerCeiling("MatchIngestion", null);
    }

    [Fact]
    public async Task RunAsync_UnsetBatchSize_SplitsTheLanesBatchAcrossTheRoutes()
    {
        var process = new ScriptedProcess();
        var pool = BuildPool(process, ["EUW1", "KR", "NA1"], out _, out _, batchSize: null, laneBatchSize: 75);

        await pool.RunAsync(runOnce: true, CancellationToken.None).WaitAsync(Settle);

        process.Batches.Should().OnlyContain(batch => batch.BatchSize == 25);
    }

    [Fact]
    public async Task RunAsync_KeepsClaimingUntilCancelled_PausingOnlyAfterAnEmptyClaim()
    {
        var time = new FakeTimeProvider();
        var process = new ScriptedProcess(Worked, Worked, Empty);
        var pool = BuildPool(process, ["EUW1"], out _, out _, timeProvider: time);
        using var cts = new CancellationTokenSource();

        var running = pool.RunAsync(runOnce: false, cts.Token);

        // Two batches that found work run back to back; the empty one parks the worker on
        // its idle delay rather than spinning on an empty queue.
        await process.WaitForBatchesAsync(3);
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        process.Batches.Should().HaveCount(3);

        await process.WaitForBatchesAsync(4, time, TimeSpan.FromSeconds(60));

        await StopAsync(cts, running);
    }

    [Fact]
    public async Task RunAsync_AFailedBatch_DoesNotEndTheWorker()
    {
        var time = new FakeTimeProvider();
        var process = new ScriptedProcess(Fail, Worked, Empty);
        var pool = BuildPool(process, ["EUW1"], out _, out _, timeProvider: time);
        using var cts = new CancellationTokenSource();

        var running = pool.RunAsync(runOnce: false, cts.Token);

        await process.WaitForBatchesAsync(2, time, TimeSpan.FromSeconds(30));

        await StopAsync(cts, running);
    }

    [Fact]
    public async Task RunAsync_CancelledMidBatch_ReleasesThatRoutesClaimsAndNoOtherRoutes()
    {
        var process = new ScriptedProcess(BlockUntilCancelled);
        var pool = BuildPool(process, ["EUW1", "EUN1"], out _, out var claims);
        using var cts = new CancellationTokenSource();

        var running = pool.RunAsync(runOnce: false, cts.Token);
        await process.WaitForBatchesAsync(1);

        await cts.CancelAsync();
        await FluentActions.Awaiting(() => running.WaitAsync(Settle)).Should().ThrowAsync<OperationCanceledException>();

        // Released on the way out, narrowed to the platforms this worker owns, so the claims
        // a shutdown interrupted are claimable again without waiting for the next boot.
        await claims.Received(1).ReleaseOrphanedClaimsAsync(
            Arg.Is<IReadOnlyCollection<string>>(platforms => platforms.SequenceEqual(new[] { "EUW1", "EUN1" })),
            Arg.Any<CancellationToken>());
        await claims.DidNotReceive().ReleaseOrphanedClaimsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_UnderBackpressure_FinishesTheBatchInFlightAndHoldsTheNextClaimUntilRelease()
    {
        // #1459: the brake degrades throughput, never correctness. A batch already claimed is
        // fetched and written to the end, and only the next claim waits for the resource.
        var time = new FakeTimeProvider();
        var probe = Substitute.For<IHostResourceProbe>();
        var reading = new HostResourceReading(80, 80);
        probe.Read(Arg.Any<string>()).Returns(_ => reading);
        var backpressure = new IngestionBackpressure(
            probe,
            Microsoft.Extensions.Options.Options.Create(new IngestionBackpressureOptions { MinFreeDiskPercent = 50 }),
            time,
            NullLogger<IngestionBackpressure>.Instance);

        using var inFlight = new ManualResetEventSlim();
        var finished = 0;
        var process = new ScriptedProcess(
            ct =>
            {
                inFlight.Wait(ct);
                Interlocked.Increment(ref finished);
                return Worked(ct);
            },
            Worked,
            Empty);
        var pool = BuildPool(process, ["EUW1"], out _, out var claims, timeProvider: time, backpressure: backpressure);
        using var cts = new CancellationTokenSource();

        var running = pool.RunAsync(runOnce: false, cts.Token);
        await process.WaitForBatchesAsync(1);

        reading = new HostResourceReading(80, 20);
        inFlight.Set();
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        time.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        finished.Should().Be(1, "the batch claimed before the resource dropped runs to its end");
        process.Batches.Should().HaveCount(1, "no claim is made while the disk is below its threshold");
        backpressure.IsActive.Should().BeTrue();

        reading = new HostResourceReading(80, 80);
        await process.WaitForBatchesAsync(2, time, TimeSpan.FromSeconds(30));

        await StopAsync(cts, running);
        await claims.DidNotReceive().ReleaseOrphanedClaimsAsync(
            Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Stops the pool and waits for it. A worker cancelled between two batches ends its loop
    /// quietly and one cancelled inside a wait throws; both are a clean stop.
    /// </summary>
    private static async Task StopAsync(CancellationTokenSource cts, Task running)
    {
        await cts.CancelAsync();
        try
        {
            await running.WaitAsync(Settle);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static IProcessRunSummary? Worked(CancellationToken ct)
        => new MatchIngestionSummary(3, 10, 0, 10, 0, 3, 0, 0, 0, []);

    private static IProcessRunSummary? Empty(CancellationToken ct)
        => new MatchIngestionSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, []);

    private static IProcessRunSummary? Fail(CancellationToken ct)
        => throw new InvalidOperationException("database unreachable");

    private static IProcessRunSummary? BlockUntilCancelled(CancellationToken ct)
    {
        ct.WaitHandle.WaitOne();
        ct.ThrowIfCancellationRequested();
        return null;
    }

    private static MatchFetchWorkerPool BuildPool(
        ScriptedProcess process,
        List<string> platforms,
        out IRiotRateLimiter limiter,
        out IMatchClaimService claims,
        int? batchSize = 5,
        int laneBatchSize = 50,
        TimeProvider? timeProvider = null,
        IngestionBackpressure? backpressure = null)
    {
        var iterations = new IterationContext();
        var callers = new CallerContext();
        process.Bind(iterations, callers);

        claims = Substitute.For<IMatchClaimService>();
        var services = new ServiceCollection();
        services.AddScoped<MatchIngestionScope>();
        services.AddSingleton(claims);
        services.AddKeyedScoped<IIngestorProcess>(
            JobMode.MatchIngestionOnly,
            (provider, _) => new ScopedProcess(process, provider.GetRequiredService<MatchIngestionScope>()));

        limiter = Substitute.For<IRiotRateLimiter>();
        return new MatchFetchWorkerPool(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            iterations,
            callers,
            limiter,
            backpressure ?? Unpressured(),
            Microsoft.Extensions.Options.Options.Create(new MatchIngestionOptions { Platforms = platforms, BatchSize = laneBatchSize }),
            Microsoft.Extensions.Options.Options.Create(new MatchFetchWorkersOptions { BatchSize = batchSize }),
            TestIngestorMetrics.Create(),
            timeProvider ?? TimeProvider.System,
            NullLogger<MatchFetchWorkerPool>.Instance);
    }

    /// <summary>A brake whose host always has room, for the tests that are not about it.</summary>
    internal static IngestionBackpressure Unpressured()
    {
        var probe = Substitute.For<IHostResourceProbe>();
        probe.Read(Arg.Any<string>()).Returns(new HostResourceReading(80, 80));
        return new IngestionBackpressure(
            probe,
            Microsoft.Extensions.Options.Options.Create(new IngestionBackpressureOptions()),
            TimeProvider.System,
            NullLogger<IngestionBackpressure>.Instance);
    }

    private sealed record Batch(IReadOnlyCollection<string> Platforms, int BatchSize, string? Caller, JobMode? Mode);

    /// <summary>Plays one scripted outcome per batch, the last one repeating.</summary>
    private sealed class ScriptedProcess(params Func<CancellationToken, IProcessRunSummary?>[] script)
    {
        private readonly ConcurrentQueue<Batch> _batches = new();
        private int _next;
        private IIterationContext? _iterations;
        private ICallerContext? _callers;

        public IReadOnlyCollection<Batch> Batches => _batches;

        public void Bind(IIterationContext iterations, ICallerContext callers)
        {
            _iterations = iterations;
            _callers = callers;
        }

        public Task<IProcessRunSummary?> RunAsync(MatchIngestionScope scope, CancellationToken ct)
        {
            _batches.Enqueue(new Batch(scope.Platforms!, scope.BatchSize!.Value, _callers!.CurrentCaller, _iterations!.CurrentJobMode));
            if (script.Length == 0)
            {
                return Task.FromResult(Worked(ct));
            }

            var step = script[Math.Min(Interlocked.Increment(ref _next) - 1, script.Length - 1)];
            return Task.Run(() => step(ct), CancellationToken.None);
        }

        /// <summary>
        /// Waits for <paramref name="count"/> batches, moving <paramref name="time"/> on by
        /// <paramref name="step"/> while it waits: the worker's pause starts at a moment the
        /// test cannot observe, and a clock advanced before the timer exists never fires it.
        /// </summary>
        public async Task WaitForBatchesAsync(int count, FakeTimeProvider? time = null, TimeSpan step = default)
        {
            var deadline = DateTime.UtcNow + Settle;
            while (_batches.Count < count)
            {
                DateTime.UtcNow.Should().BeBefore(deadline, $"the worker should have run {count} batch(es) by now");
                await Task.Delay(TimeSpan.FromMilliseconds(10));
                time?.Advance(step);
            }
        }
    }

    private sealed class ScopedProcess(ScriptedProcess script, MatchIngestionScope scope) : IIngestorProcess
    {
        public string Name => "MatchIngestion";

        public Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct) => script.RunAsync(scope, ct);
    }
}
