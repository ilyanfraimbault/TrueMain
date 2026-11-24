using AwesomeAssertions;
using Core.Options;
using Data.Entities;
using Ingestor.Persistence;
using Ingestor.Processes;
using Ingestor.Processes.Components.Coverage;
using Ingestor.Processes.Components.MainAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace TrueMain.UnitTests;

/// <summary>
/// #2669: Postgres can pick a batch's write transaction as a deadlock victim (40P01) against
/// another writer of riot_accounts. It rolls the whole transaction back, so the batch is
/// replayed once instead of failing every remaining batch of the run.
/// </summary>
public sealed class MainAnalysisDeadlockReplayTests
{
    private static readonly AccountKey Account = new("KR", "puuid-deadlock-1");

    private readonly IDataSessionFactory _sessionFactory = Substitute.For<IDataSessionFactory>();
    private readonly IRiotAccountRepository _riotAccounts = Substitute.For<IRiotAccountRepository>();

    public MainAnalysisDeadlockReplayTests()
    {
        var session = Substitute.For<IDataSession>();
        var stats = Substitute.For<IMainChampionStatRepository>();
        var participants = Substitute.For<IMatchParticipantRepository>();
        var candidates = Substitute.For<IMainCandidateRepository>();

        _riotAccounts.GetAccountsForMainAnalysisAsync(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Account]);
        stats.GetByAccountsAsync(Arg.Any<IReadOnlyCollection<AccountKey>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<AccountKey, List<MainChampionStat>>());
        participants.GetRecentParticipantsByAccountsAsync(
                Arg.Any<IReadOnlyCollection<AccountKey>>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<AccountKey, List<ParticipantRow>>());
        candidates.SetStatusForAccountsAsync(
                Arg.Any<IReadOnlyCollection<AccountKey>>(), Arg.Any<MainCandidateStatus>(),
                Arg.Any<MainCandidateStatus>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AccountKey>());

        session.RiotAccounts.Returns(_riotAccounts);
        session.MainChampionStats.Returns(stats);
        session.MatchParticipants.Returns(participants);
        session.MainCandidates.Returns(candidates);
        session.BeginTransactionAsync(Arg.Any<CancellationToken>()).Returns(Substitute.For<IDataTransaction>());
        _sessionFactory.CreateAsync(Arg.Any<CancellationToken>()).Returns(session);
    }

    [Fact]
    public async Task RunCoreAsync_ReplaysTheBatchOnce_WhenItsTransactionIsADeadlockVictim()
    {
        _riotAccounts.StampLastMainCalcAsync(Arg.Any<IReadOnlyCollection<AccountKey>>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw WrappedDeadlock(), _ => Task.FromResult(1));

        var summary = await CreateProcess().RunCoreAsync(RunInput.Whole, CancellationToken.None);

        summary.Should().NotBeNull();
        await _riotAccounts.Received(2).StampLastMainCalcAsync(
            Arg.Any<IReadOnlyCollection<AccountKey>>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _sessionFactory.Received(4).CreateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCoreAsync_Fails_WhenTheReplayIsADeadlockVictimToo()
    {
        _riotAccounts.StampLastMainCalcAsync(Arg.Any<IReadOnlyCollection<AccountKey>>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Throws(new PostgresException("deadlock detected", "ERROR", "ERROR", PostgresErrorCodes.DeadlockDetected));

        var act = () => CreateProcess().RunCoreAsync(RunInput.Whole, CancellationToken.None);

        await act.Should().ThrowAsync<PostgresException>();
        await _riotAccounts.Received(2).StampLastMainCalcAsync(
            Arg.Any<IReadOnlyCollection<AccountKey>>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCoreAsync_DoesNotReplay_WhenTheFailureIsNotADeadlock()
    {
        _riotAccounts.StampLastMainCalcAsync(Arg.Any<IReadOnlyCollection<AccountKey>>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException(
                "transient",
                new PostgresException("canceling statement", "ERROR", "ERROR", PostgresErrorCodes.QueryCanceled)));

        var act = () => CreateProcess().RunCoreAsync(RunInput.Whole, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _riotAccounts.Received(1).StampLastMainCalcAsync(
            Arg.Any<IReadOnlyCollection<AccountKey>>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    // The shape seen in production: EF's execution strategy wraps the Postgres error.
    private static InvalidOperationException WrappedDeadlock()
        => new(
            "An exception has been raised that is likely due to a transient failure.",
            new PostgresException("deadlock detected", "ERROR", "ERROR", PostgresErrorCodes.DeadlockDetected));

    private MainAnalysisProcess CreateProcess()
    {
        var calculator = Substitute.For<IMainStatsCalculator>();
        calculator.Calculate(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<ParticipantRow>>(),
                Arg.Any<MainAnalysisOptions>(), Arg.Any<ChampionCoverageSnapshot>(), Arg.Any<DateTime>())
            .Returns([]);
        var coverage = Substitute.For<IChampionCoverageProvider>();
        coverage.GetSnapshotAsync(Arg.Any<IDataSession>(), Arg.Any<CancellationToken>())
            .Returns(ChampionCoverageSnapshot.Empty);

        return new MainAnalysisProcess(
            NullLogger<MainAnalysisProcess>.Instance,
            _sessionFactory,
            calculator,
            Substitute.For<IMainDemotionPolicy>(),
            coverage,
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions()));
    }
}
