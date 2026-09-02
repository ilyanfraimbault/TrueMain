using AwesomeAssertions;
using Core.Lol.Identifiers;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Processes.Summaries;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// Riot meters its application limit per routing value, so a claim batch that walks its
/// accounts in one serial loop spends the run inside a single region's allowance while
/// the others idle (#1359). These tests pin the two properties the fan-out has to keep:
/// the platforms really do run at the same time, and one platform's failure stays its own.
/// </summary>
public sealed class MatchIngestionPlatformConcurrencyTests
{
    [Fact]
    public async Task RunCoreAsync_IngestsPlatformsConcurrently()
    {
        // Each platform's first account blocks until every platform has reached this
        // point. A serial loop can never satisfy that, so the barrier is the assertion:
        // if the platforms are not concurrent, the run never completes and the timeout
        // below fails the test.
        using var allPlatformsStarted = new CountdownEvent(3);
        var snapshotWriter = SucceedingSnapshotWriter(onPrepare: () =>
        {
            allPlatformsStarted.Signal();
            allPlatformsStarted.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue(
                "every platform must be ingesting at the same time");
        });

        var summary = await RunAsync(
            snapshotWriter,
            ["EUW1", "KR", "NA1"],
            [
                new AccountKey("EUW1", "euw-1"),
                new AccountKey("KR", "kr-1"),
                new AccountKey("NA1", "na-1")
            ]).WaitAsync(TimeSpan.FromSeconds(30));

        var ingestion = (MatchIngestionSummary)summary!;
        ingestion.AccountsProcessed.Should().Be(3);
        ingestion.Errors.Should().Be(0);
    }

    [Fact]
    public async Task RunCoreAsync_KeepsOnePlatformsFailureOffTheOthers()
    {
        var snapshotWriter = SucceedingSnapshotWriter();
        snapshotWriter.PrepareAsync(
                Arg.Any<IDataSession>(), "KR", Arg.Any<string>(),
                Arg.Any<RegionalRoute>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<SnapshotIngestionPlan>(new InvalidOperationException("kr boom")));

        var summary = await RunAsync(
            snapshotWriter,
            ["EUW1", "KR", "NA1"],
            [
                new AccountKey("EUW1", "euw-1"),
                new AccountKey("KR", "kr-1"),
                new AccountKey("NA1", "na-1")
            ]).WaitAsync(TimeSpan.FromSeconds(30));

        var ingestion = (MatchIngestionSummary)summary!;
        ingestion.Errors.Should().Be(1);
        ingestion.AccountsProcessed.Should().Be(2, "the two healthy platforms still finished");
        ingestion.ByPlatform.Select(platform => platform.Platform).Should().BeEquivalentTo(["EUW1", "NA1"]);
    }

    [Fact]
    public async Task RunCoreAsync_ReportsEachPlatformsOwnTally()
    {
        var summary = await RunAsync(
            SucceedingSnapshotWriter(),
            ["EUW1", "KR"],
            [
                new AccountKey("EUW1", "euw-1"),
                new AccountKey("EUW1", "euw-2"),
                new AccountKey("KR", "kr-1")
            ]).WaitAsync(TimeSpan.FromSeconds(30));

        var ingestion = (MatchIngestionSummary)summary!;
        ingestion.AccountsProcessed.Should().Be(3);
        ingestion.ByPlatform.Single(platform => platform.Platform == "EUW1").AccountsProcessed.Should().Be(2);
        ingestion.ByPlatform.Single(platform => platform.Platform == "KR").AccountsProcessed.Should().Be(1);
    }

    private static async Task<IProcessRunSummary?> RunAsync(
        IMatchSnapshotWriter snapshotWriter,
        IReadOnlyList<string> platforms,
        IReadOnlyList<AccountKey> claimed)
    {
        // A fresh session per call, as the real factory hands out: the platforms run
        // concurrently, so a single shared substitute would be the test's own race.
        var sessionFactory = Substitute.For<IDataSessionFactory>();
        sessionFactory.CreateAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var session = Substitute.For<IDataSession>();
            session.BeginTransactionAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(Substitute.For<IDbContextTransaction>()));
            return Task.FromResult(session);
        });

        var matchClaimService = Substitute.For<IMatchClaimService>();
        matchClaimService.ClaimAsync(
                Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<int>(), Arg.Any<double>(),
                Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(claimed.ToList()));

        var accountValidationService = Substitute.For<IAccountValidationService>();
        accountValidationService.ValidateAsync(Arg.Any<AccountKey>(), Arg.Any<CancellationToken>()).Returns(true);

        var process = new MatchIngestionProcess(
            NullLogger<MatchIngestionProcess>.Instance,
            sessionFactory,
            matchClaimService,
            snapshotWriter,
            Substitute.For<ITimelineIngestionService>(),
            accountValidationService,
            Microsoft.Extensions.Options.Options.Create(new MatchIngestionOptions
            {
                Platforms = [.. platforms],
                BatchSize = claimed.Count
            }));

        return await process.RunCoreAsync(CancellationToken.None);
    }

    private static IMatchSnapshotWriter SucceedingSnapshotWriter(Action? onPrepare = null)
    {
        var snapshotWriter = Substitute.For<IMatchSnapshotWriter>();
        snapshotWriter.PrepareAsync(
                Arg.Any<IDataSession>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<RegionalRoute>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                onPrepare?.Invoke();
                return Task.FromResult(new SnapshotIngestionPlan([], [], [], new Dictionary<AccountKey, Data.Entities.RiotAccount>(), null));
            });
        snapshotWriter.WriteAsync(
                Arg.Any<IDataSession>(), Arg.Any<SnapshotIngestionPlan>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SnapshotIngestionResult([], [], 0, 0)));
        return snapshotWriter;
    }
}
