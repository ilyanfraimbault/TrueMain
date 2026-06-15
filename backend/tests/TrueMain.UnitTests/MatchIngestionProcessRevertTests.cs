using AwesomeAssertions;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.MatchIngestion;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace TrueMain.UnitTests;

public sealed class MatchIngestionProcessRevertTests
{
    [Fact]
    public async Task RunCoreAsync_WhenRevertFailsAfterIngestionError_DoesNotThrowAndKeepsProcessingBatch()
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

        var process = new MatchIngestionProcess(
            NullLogger<MatchIngestionProcess>.Instance,
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
    }
}
