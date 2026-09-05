using AwesomeAssertions;
using Core.Lol.Identifiers;
using Data.Ops.Mongo;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Summaries;
using Ingestor.Riot;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TrueMain.UnitTests.Fixtures;

namespace TrueMain.UnitTests;

/// <summary>
/// The activity pass's cadence guard (#1474): the interval is measured from the last run that
/// did its work, and a zero interval keeps the every-iteration behaviour.
/// </summary>
public sealed class MainActivityCadenceTests
{
    private static readonly DateTime FixedNow = new(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RunCoreAsync_Skips_WhenLastRunWithinMinRunInterval()
    {
        var harness = new Harness(lastCompletedRunUtc: FixedNow.AddMinutes(-20));

        var summary = await harness.Process(TimeSpan.FromHours(1)).RunCoreAsync(CancellationToken.None);

        summary.Should().BeOfType<SkippedSummary>().Which.Skipped.Should().BeTrue();
        await harness.RiotAccounts.DidNotReceive().GetAccountsForActivityCheckAsync(
            Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCoreAsync_Runs_WhenLastRunOlderThanMinRunInterval()
    {
        var harness = new Harness(lastCompletedRunUtc: FixedNow.AddHours(-2));

        var summary = await harness.Process(TimeSpan.FromHours(1)).RunCoreAsync(CancellationToken.None);

        summary.Should().BeOfType<NoWorkSummary>();
        await harness.RiotAccounts.Received(1).GetAccountsForActivityCheckAsync(
            Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCoreAsync_Runs_WhenMinRunIntervalIsZero()
    {
        var harness = new Harness(lastCompletedRunUtc: FixedNow);

        await harness.Process(TimeSpan.Zero).RunCoreAsync(CancellationToken.None);

        await harness.RiotAccounts.Received(1).GetAccountsForActivityCheckAsync(
            Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private sealed class Harness
    {
        public IRiotAccountRepository RiotAccounts { get; } = Substitute.For<IRiotAccountRepository>();
        private readonly IDataSessionFactory _sessionFactory = Substitute.For<IDataSessionFactory>();
        private readonly IProcessRunStore _processRunStore = Substitute.For<IProcessRunStore>();

        public Harness(DateTime lastCompletedRunUtc)
        {
            // No account due -> the process returns a NoWorkSummary right after the selection,
            // which is all the cadence tests need to observe.
            RiotAccounts.GetAccountsForActivityCheckAsync(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new List<AccountKey>()));

            var session = Substitute.For<IDataSession>();
            session.RiotAccounts.Returns(RiotAccounts);
            _sessionFactory.CreateAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(session));

            _processRunStore.GetLastCompletedRunStartAsync("MainActivity", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<DateTime?>(lastCompletedRunUtc));
        }

        public MainActivityProcess Process(TimeSpan minRunInterval) => new(
            NullLogger<MainActivityProcess>.Instance,
            Substitute.For<IRiotPlatformClient>(),
            _sessionFactory,
            _processRunStore,
            new FixedTimeProvider(FixedNow),
            Microsoft.Extensions.Options.Options.Create(new MainActivityOptions
            {
                BatchSize = 50,
                MinRunInterval = minRunInterval
            }));
    }
}
