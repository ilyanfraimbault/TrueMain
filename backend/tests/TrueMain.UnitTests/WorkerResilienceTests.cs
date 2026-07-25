using AwesomeAssertions;
using Ingestor;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace TrueMain.UnitTests;

public sealed class WorkerResilienceTests
{
    [Fact]
    public async Task ExecuteAsync_LogsAndContinues_WhenProcessThrowsTransientException()
    {
        var failingProcess = new FaultyProcess(throwOnCall: 1);
        var serviceProvider = BuildServiceProvider(failingProcess);
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var jobOptions = Microsoft.Extensions.Options.Options.Create(new JobOptions
        {
            Mode = "DiscoveryOnly",
            RunOnce = true
        });

        using var worker = new Worker(
            NullLogger<Worker>.Instance,
            serviceProvider,
            jobOptions,
            new IterationContext(),
            lifetime,
            TestIngestorMetrics.Create());

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        // Worker swallows the exception so the host can keep running. RunOnce
        // means a single iteration was attempted.
        failingProcess.CallCount.Should().Be(1);
        lifetime.Received(1).StopApplication();
    }

    [Fact]
    public async Task ExecuteAsync_CreatesAFreshScopePerProcess_WhenRunningFullSequence()
    {
        // The full sequence drives twelve processes. Issue #256 requires each one
        // to resolve from its own scope (its own DbContext / scoped services)
        // rather than sharing a single scope across the whole run.
        var processNames = new[]
        {
            "Discovery",
            "ManualSeed",
            "Harvest",
            "Scoring",
            "MatchIngestion",
            "MatchTeamPositionCorrection",
            "MainAnalysis",
            "MatchParticipantEloBracketEnrichment",
            "ChampionPatternAggregation",
            "ChampionMatchupLeadAggregation",
            "ChampionPowerspikeAggregation",
            "AccountRefresh",
            "MatchDataRetention"
        };

        var services = new ServiceCollection();
        foreach (var name in processNames)
        {
            services.AddSingleton<IIngestorProcess>(new NamedProcess(name));
        }

        using var provider = services.BuildServiceProvider();
        var countingScopeFactory = new CountingScopeFactory(
            provider.GetRequiredService<IServiceScopeFactory>());
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var jobOptions = Microsoft.Extensions.Options.Options.Create(new JobOptions
        {
            Mode = "Full",
            RunOnce = true
        });

        using var worker = new Worker(
            NullLogger<Worker>.Instance,
            countingScopeFactory,
            jobOptions,
            new IterationContext(),
            lifetime,
            TestIngestorMetrics.Create());

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        // One scope is created and disposed per process in the sequence, plus one
        // up front for the startup orphaned-run reconciliation (which runs once
        // before the main loop, in its own scope).
        const int reconciliationScopes = 1;
        countingScopeFactory.ScopesCreated.Should().Be(processNames.Length + reconciliationScopes);
        countingScopeFactory.ScopesDisposed.Should().Be(processNames.Length + reconciliationScopes);
        lifetime.Received(1).StopApplication();
    }

    [Fact]
    public async Task ExecuteAsync_LetsCancellationPropagate_WhenCancellationRequested()
    {
        var process = new FaultyProcess(throwOnCall: int.MaxValue);
        var serviceProvider = BuildServiceProvider(process);
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var jobOptions = Microsoft.Extensions.Options.Options.Create(new JobOptions
        {
            Mode = "DiscoveryOnly",
            RunOnce = false,
            IntervalMinutes = 60
        });

        using var worker = new Worker(
            NullLogger<Worker>.Instance,
            serviceProvider,
            jobOptions,
            new IterationContext(),
            lifetime,
            TestIngestorMetrics.Create());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await worker.StartAsync(cts.Token);

        // Cooperative cancellation during the inter-run delay must surface as an
        // OperationCanceledException rather than being swallowed, so the host can
        // observe the natural shutdown flow.
        var act = async () => await worker.ExecuteTask!;
        await act.Should().ThrowAsync<OperationCanceledException>();

        // The worker no longer forces StopApplication on shutdown; the host owns
        // the lifecycle once cancellation has been requested.
        lifetime.DidNotReceive().StopApplication();
    }

    private static IServiceScopeFactory BuildServiceProvider(FaultyProcess process)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIngestorProcess>(process);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private sealed class NamedProcess(string name) : IIngestorProcess
    {
        public string Name => name;

        public Task<object?> RunCoreAsync(CancellationToken ct) => Task.FromResult<object?>(null);
    }

    // Wraps the real scope factory so the test can assert that the worker
    // creates (and disposes) exactly one scope per process in the sequence.
    private sealed class CountingScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        private int _scopesCreated;
        private int _scopesDisposed;

        public int ScopesCreated => Volatile.Read(ref _scopesCreated);

        public int ScopesDisposed => Volatile.Read(ref _scopesDisposed);

        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref _scopesCreated);
            return new CountingScope(inner.CreateScope(), () => Interlocked.Increment(ref _scopesDisposed));
        }

        private sealed class CountingScope(IServiceScope inner, Action onDispose) : IServiceScope, IAsyncDisposable
        {
            private int _disposed;

            public IServiceProvider ServiceProvider => inner.ServiceProvider;

            public void Dispose()
            {
                MarkDisposed();
                inner.Dispose();
            }

            public async ValueTask DisposeAsync()
            {
                MarkDisposed();
                if (inner is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else
                {
                    inner.Dispose();
                }
            }

            private void MarkDisposed()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    onDispose();
                }
            }
        }
    }

    private sealed class FaultyProcess(int throwOnCall) : IIngestorProcess
    {
        public string Name => "Discovery";

        public int CallCount { get; private set; }

        public Task<object?> RunCoreAsync(CancellationToken ct)
        {
            CallCount++;
            if (CallCount <= throwOnCall)
            {
                throw new InvalidOperationException("simulated transient failure");
            }

            return Task.FromResult<object?>(null);
        }
    }
}
