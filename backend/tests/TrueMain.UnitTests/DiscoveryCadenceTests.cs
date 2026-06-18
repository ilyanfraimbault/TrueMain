using Core.Lol.Identifiers;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.Discovery;
using Ingestor.Ranking;
using Ingestor.Riot;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TrueMain.UnitTests.Fixtures;

namespace TrueMain.UnitTests;

public sealed class DiscoveryCadenceTests
{
    // Pin the clock so the cadence comparison (now - lastRun < interval) is fully
    // deterministic: both the process's "now" and lastCompletedRunUtc derive from
    // this same instant, leaving no wall-clock window between the two captures.
    private static readonly DateTime FixedNow = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    [Fact]
    public async Task RunCoreAsync_SkipsDiscovery_WhenLastRunWithinMinRunInterval()
    {
        var harness = new Harness(lastCompletedRunUtc: FixedNow.AddHours(-1));

        await harness.Process(minRunInterval: TimeSpan.FromDays(1)).RunCoreAsync(CancellationToken.None);

        await harness.Ladder.DidNotReceive().DiscoverSummonersAsync(
            Arg.Any<PlatformRoute>(), Arg.Any<DiscoveryOptions>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCoreAsync_RunsDiscovery_WhenLastRunOlderThanMinRunInterval()
    {
        var harness = new Harness(lastCompletedRunUtc: FixedNow.AddDays(-2));

        await harness.Process(minRunInterval: TimeSpan.FromDays(1)).RunCoreAsync(CancellationToken.None);

        await harness.Ladder.Received(1).DiscoverSummonersAsync(
            Arg.Any<PlatformRoute>(), Arg.Any<DiscoveryOptions>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCoreAsync_RunsDiscovery_WhenMinRunIntervalIsZero()
    {
        var harness = new Harness(lastCompletedRunUtc: FixedNow);

        await harness.Process(minRunInterval: TimeSpan.Zero).RunCoreAsync(CancellationToken.None);

        await harness.Ladder.Received(1).DiscoverSummonersAsync(
            Arg.Any<PlatformRoute>(), Arg.Any<DiscoveryOptions>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private sealed class Harness
    {
        public ILadderDiscoveryService Ladder { get; } = Substitute.For<ILadderDiscoveryService>();
        private readonly IDataSessionFactory _sessionFactory = Substitute.For<IDataSessionFactory>();

        public Harness(DateTime lastCompletedRunUtc)
        {
            var session = Substitute.For<IDataSession>();
            var processRuns = Substitute.For<IProcessRunRepository>();
            processRuns.GetLastCompletedRunStartAsync("Discovery", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<DateTime?>(lastCompletedRunUtc));
            session.ProcessRuns.Returns(processRuns);
            _sessionFactory.CreateAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(session));

            // No summoners resolved -> the per-platform path returns immediately after the
            // ladder call, which is all these tests assert on.
            Ladder.DiscoverSummonersAsync(
                    Arg.Any<PlatformRoute>(), Arg.Any<DiscoveryOptions>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new LadderDiscoveryResult([], 0, 0)));
        }

        public DiscoveryProcess Process(TimeSpan minRunInterval) => new(
            NullLogger<DiscoveryProcess>.Instance,
            Substitute.For<IRiotPlatformClient>(),
            _sessionFactory,
            Ladder,
            Substitute.For<IAccountUpsertService>(),
            Substitute.For<ICandidateUpsertService>(),
            Substitute.For<IRankSnapshotWriter>(),
            new FixedTimeProvider(FixedNow),
            Microsoft.Extensions.Options.Options.Create(new DiscoveryOptions
            {
                Platforms = ["KR"],
                MinRunInterval = minRunInterval
            }));
    }
}
