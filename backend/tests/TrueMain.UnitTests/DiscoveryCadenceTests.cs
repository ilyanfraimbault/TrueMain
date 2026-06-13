using Core.Lol.Identifiers;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.Discovery;
using Ingestor.Ranking;
using Ingestor.Riot;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace TrueMain.UnitTests;

public sealed class DiscoveryCadenceTests
{
    [Fact]
    public async Task RunCoreAsync_SkipsDiscovery_WhenLastRunWithinMinRunInterval()
    {
        var harness = new Harness(lastCompletedRunUtc: DateTime.UtcNow.AddHours(-1));

        await harness.Process(minRunInterval: TimeSpan.FromDays(1)).RunCoreAsync(CancellationToken.None);

        await harness.Ladder.DidNotReceive().DiscoverSummonersAsync(
            Arg.Any<PlatformRoute>(), Arg.Any<DiscoveryOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCoreAsync_RunsDiscovery_WhenLastRunOlderThanMinRunInterval()
    {
        var harness = new Harness(lastCompletedRunUtc: DateTime.UtcNow.AddDays(-2));

        await harness.Process(minRunInterval: TimeSpan.FromDays(1)).RunCoreAsync(CancellationToken.None);

        await harness.Ladder.Received(1).DiscoverSummonersAsync(
            Arg.Any<PlatformRoute>(), Arg.Any<DiscoveryOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCoreAsync_RunsDiscovery_WhenMinRunIntervalIsZero()
    {
        var harness = new Harness(lastCompletedRunUtc: DateTime.UtcNow);

        await harness.Process(minRunInterval: TimeSpan.Zero).RunCoreAsync(CancellationToken.None);

        await harness.Ladder.Received(1).DiscoverSummonersAsync(
            Arg.Any<PlatformRoute>(), Arg.Any<DiscoveryOptions>(), Arg.Any<CancellationToken>());
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
                    Arg.Any<PlatformRoute>(), Arg.Any<DiscoveryOptions>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new List<DiscoveredSummoner>()));
        }

        public DiscoveryProcess Process(TimeSpan minRunInterval) => new(
            NullLogger<DiscoveryProcess>.Instance,
            Substitute.For<IRiotPlatformClient>(),
            _sessionFactory,
            Ladder,
            Substitute.For<IAccountUpsertService>(),
            Substitute.For<ICandidateUpsertService>(),
            Substitute.For<IRankSnapshotWriter>(),
            Microsoft.Extensions.Options.Options.Create(new DiscoveryOptions
            {
                Platforms = ["KR"],
                MinRunInterval = minRunInterval
            }));
    }
}
