using AwesomeAssertions;
using Ingestor;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Summaries;
using Ingestor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// #1229: the ingestor's file heartbeat is a liveness signal, not a progress one. It used to
/// be touched once per loop iteration, so it went stale for a whole <c>Job:IntervalMinutes</c>
/// plus a whole Full pass and the healthcheck had to tolerate 6 h of silence — unable to
/// detect anything short of a process dead for a quarter of a day. A dedicated loop now
/// refreshes it for the worker's whole lifetime.
/// <para>
/// The property that buys is that the beat is <em>independent of the pass</em>, so that is
/// what these tests pin: a pass that never returns must not stop the file from being
/// refreshed, and the loop must still be joined on shutdown rather than leaking or wedging
/// the host. Both tests hold a pass open forever on purpose — that is the wedged process the
/// healthcheck exists to catch.
/// </para>
/// </summary>
public sealed class WorkerHeartbeatTests : IDisposable
{
    private const string HeartbeatEnvironmentVariable = "INGESTOR_HEARTBEAT_PATH";
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string heartbeatPath =
        Path.Combine(Path.GetTempPath(), $"ingestor-heartbeat-{Guid.NewGuid():N}");

    // The variable is process-global, so every test that touches it lives in this one class:
    // xUnit runs the tests of a class sequentially, and nothing else reads it.
    public WorkerHeartbeatTests()
        => Environment.SetEnvironmentVariable(HeartbeatEnvironmentVariable, heartbeatPath);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(HeartbeatEnvironmentVariable, null);
        File.Delete(heartbeatPath);
    }

    [Fact]
    public async Task ExecuteAsync_KeepsRefreshingTheHeartbeat_WhileAPassNeverReturns()
    {
        var pass = new BlockingProcess();
        var time = new FakeTimeProvider();
        using var worker = BuildWorker(pass, time);

        await worker.StartAsync(CancellationToken.None);

        // The first beat is written before the pass starts, so a Full pass that takes
        // minutes cannot leave the container looking dead from its very first second.
        var first = await WaitForBeatAfterAsync(null);
        await pass.Started;

        // Every advance is one interval. The pass is still blocked throughout — if the
        // beat were tied to it, as it was before #1229, the file would never move again.
        var second = await AdvanceAndWaitAsync(time, first);
        var third = await AdvanceAndWaitAsync(time, second);

        new[] { first, second, third }.Should().OnlyHaveUniqueItems();
        pass.HasCompleted.Should().BeFalse();

        await StopAsync(worker);
    }

    [Fact]
    public async Task ExecuteAsync_JoinsTheHeartbeatLoop_WhenTheHostShutsDown()
    {
        var pass = new BlockingProcess();
        var time = new FakeTimeProvider();
        using var worker = BuildWorker(pass, time);

        await worker.StartAsync(CancellationToken.None);
        await WaitForBeatAfterAsync(null);
        await pass.Started;

        // The loop is awaited in ExecuteAsync's `finally`. If cancelling failed to end it,
        // or the join deadlocked, this never returns — which is the failure this asserts
        // against, since a worker that cannot stop wedges every redeploy.
        var stop = StopAsync(worker);
        await stop.WaitAsync(Patience);

        // And it really is over: advancing past several more intervals writes nothing.
        // Read a *settled* value first — `File.WriteAllTextAsync` truncates before it
        // writes, so a read racing the last beat can legitimately come back short and
        // make this look like a change that never happened.
        var last = await ReadSettledAsync();
        time.Advance(HeartbeatInterval * 5);
        await Task.Delay(100, CancellationToken.None);
        (await ReadSettledAsync()).Should().Be(last);
    }

    private Worker BuildWorker(IIngestorProcess pass, TimeProvider time)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton(JobMode.DiscoveryOnly, pass);
        services.AddSingleton(Substitute.For<IProcessRunRecorder>());

        var jobOptions = Microsoft.Extensions.Options.Options.Create(new JobOptions
        {
            Mode = "DiscoveryOnly",
            RunOnce = false,
            IntervalMinutes = 60
        });

        return new Worker(
            NullLogger<Worker>.Instance,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            jobOptions,
            new IterationContext(),
            new CallerContext(),
            Substitute.For<IHostApplicationLifetime>(),
            TestIngestorMetrics.Create(),
            time);
    }

    /// <summary>
    /// Advance one interval and wait for the beat it triggers. The write is asynchronous, so
    /// the advance only schedules it — polling for the change is what makes the assertion
    /// about the beat rather than about the scheduler's timing.
    /// </summary>
    private async Task<string> AdvanceAndWaitAsync(FakeTimeProvider time, string previous)
    {
        time.Advance(HeartbeatInterval);
        return await WaitForBeatAfterAsync(previous);
    }

    /// <summary>
    /// The file's content once two consecutive reads agree. The writer truncates before it
    /// writes, so a single read can catch a half-written stamp — which is a race in the test,
    /// never a fact about the worker.
    /// </summary>
    private async Task<string> ReadSettledAsync()
    {
        var deadline = DateTime.UtcNow + Patience;
        var previous = string.Empty;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var current = await File.ReadAllTextAsync(heartbeatPath, CancellationToken.None);
                if (current.Length > 0 && current == previous)
                {
                    return current;
                }

                previous = current;
            }
            catch (IOException)
            {
                // Being rewritten under us. Retry.
            }

            await Task.Delay(10, CancellationToken.None);
        }

        throw new TimeoutException($"Heartbeat at {heartbeatPath} never settled within {Patience}.");
    }

    private async Task<string> WaitForBeatAfterAsync(string? previous)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var current = await File.ReadAllTextAsync(heartbeatPath, CancellationToken.None);
                // A partially written file reads as a short string; only a complete,
                // different stamp counts as a beat.
                if (current.Length > 0 && current != previous)
                {
                    return current;
                }
            }
            catch (IOException)
            {
                // Not created yet, or being rewritten under us. Retry.
            }

            await Task.Delay(10, CancellationToken.None);
        }

        throw new TimeoutException($"No heartbeat written to {heartbeatPath} within {Patience}.");
    }

    private static async Task StopAsync(Worker worker)
    {
        try
        {
            await worker.StopAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // The blocked pass observes the shutdown token and cancels out. That is the
            // cooperative shutdown path (#255), not a failure.
        }
    }

    /// <summary>A pass that never returns — the wedged process the healthcheck exists for.</summary>
    private sealed class BlockingProcess : IIngestorProcess
    {
        private readonly TaskCompletionSource started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => "Blocking";

        public Task Started => started.Task;

        public bool HasCompleted { get; private set; }

        public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            HasCompleted = true;
            return null;
        }
    }
}
