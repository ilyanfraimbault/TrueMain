using FluentAssertions;
using Ingestor;
using Ingestor.Options;
using Ingestor.Processes;
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

        using var worker = new Worker(NullLogger<Worker>.Instance, serviceProvider, jobOptions, lifetime);

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        // Worker swallows the exception so the host can keep running. RunOnce
        // means a single iteration was attempted.
        failingProcess.CallCount.Should().Be(1);
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

        using var worker = new Worker(NullLogger<Worker>.Instance, serviceProvider, jobOptions, lifetime);
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
