using AwesomeAssertions;
using Data.Logging;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.MatchIngestion;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace TrueMain.UnitTests;

public sealed class MatchIngestionProcessRevertTests
{
    [Fact]
    public async Task RunCoreAsync_WhenRevertFailsAfterIngestionError_SurfacesOpsEventAndKeepsProcessingBatch()
    {
        var sessionFactory = Substitute.For<IDataSessionFactory>();
        // Fail ingestion for every account before any session work happens, so the
        // process falls into its revert path.
        sessionFactory.CreateAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<IDataSession>(new InvalidOperationException("ingest boom")));

        var matchClaimService = Substitute.For<IMatchClaimService>();
        matchClaimService.ClaimAsync(
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<int>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<AccountKey>
            {
                new("KR", "puuid-1"),
                new("KR", "puuid-2")
            }));

        var accountValidationService = Substitute.For<IAccountValidationService>();
        // The revert itself fails: previously this exception escaped the loop and
        // aborted the rest of the batch (#263). It must now be surfaced and swallowed.
        accountValidationService.RevertAsync(Arg.Any<AccountKey>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("revert boom")));

        var logger = new CapturingLogger<MatchIngestionProcess>();

        var process = new MatchIngestionProcess(
            logger,
            sessionFactory,
            matchClaimService,
            Substitute.For<IMatchSnapshotWriter>(),
            Substitute.For<ITimelineIngestionService>(),
            accountValidationService,
            Microsoft.Extensions.Options.Options.Create(new MatchIngestionOptions
            {
                Platforms = ["KR"],
                BatchSize = 2
            }));

        var act = async () => await process.RunCoreAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();

        // A revert failure for the first account must not stop the second from being
        // attempted — proof the loop kept going instead of bubbling the exception.
        await accountValidationService.Received(2)
            .RevertAsync(Arg.Any<AccountKey>(), Arg.Any<CancellationToken>());

        // The failure must be observable: one MatchRevertFailed ops event per account,
        // each carrying both the revert failure and the original ingestion cause.
        var revertFailures = logger.Entries
            .Where(entry => entry.EventId == OpsEvents.MatchRevertFailed && entry.Level == LogLevel.Error)
            .ToList();
        revertFailures.Should().HaveCount(2);
        revertFailures.Should().AllSatisfy(entry =>
            entry.Exception.Should().BeOfType<AggregateException>()
                .Which.InnerExceptions.Should().HaveCount(2));
    }

    [Fact]
    public async Task RunCoreAsync_WhenCancellationRequestedDuringRevert_PropagatesInsteadOfSwallowing()
    {
        var sessionFactory = Substitute.For<IDataSessionFactory>();
        sessionFactory.CreateAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<IDataSession>(new InvalidOperationException("ingest boom")));

        var matchClaimService = Substitute.For<IMatchClaimService>();
        matchClaimService.ClaimAsync(
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<int>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<AccountKey> { new("KR", "puuid-1") }));

        var accountValidationService = Substitute.For<IAccountValidationService>();
        // A cooperative shutdown surfaces as an OperationCanceledException from the
        // revert; it must propagate, not be logged as a revert failure and swallowed.
        accountValidationService.RevertAsync(Arg.Any<AccountKey>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new OperationCanceledException()));

        var logger = new CapturingLogger<MatchIngestionProcess>();

        var process = new MatchIngestionProcess(
            logger,
            sessionFactory,
            matchClaimService,
            Substitute.For<IMatchSnapshotWriter>(),
            Substitute.For<ITimelineIngestionService>(),
            accountValidationService,
            Microsoft.Extensions.Options.Options.Create(new MatchIngestionOptions
            {
                Platforms = ["KR"],
                BatchSize = 1
            }));

        var act = async () => await process.RunCoreAsync(CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        logger.Entries.Should().NotContain(entry => entry.EventId == OpsEvents.MatchRevertFailed);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, EventId EventId, Exception? Exception)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, eventId, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
