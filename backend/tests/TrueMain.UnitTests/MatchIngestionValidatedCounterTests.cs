using AwesomeAssertions;
using Data.Logging;
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
/// <c>MatchIngestionSummary.AccountsValidated</c> (#1024) is what the candidate
/// funnel's validated series is built from, so the counter has to follow the
/// per-account outcome rather than the batch size: an account that had nothing left
/// to promote must not inflate it, and a failed one must not either.
/// </summary>
public sealed class MatchIngestionValidatedCounterTests
{
    [Fact]
    public async Task RunCoreAsync_CountsOnlyTheAccountsThatActuallyHadCandidatesPromoted()
    {
        var accountValidationService = Substitute.For<IAccountValidationService>();
        accountValidationService
            .ValidateAsync(new AccountKey("KR", "puuid-promoted"), Arg.Any<CancellationToken>())
            .Returns(true);
        // Already promoted, or reverted out from under the claim: the ingestion ran,
        // but no candidate row moved to Validated.
        accountValidationService
            .ValidateAsync(new AccountKey("KR", "puuid-nothing-to-promote"), Arg.Any<CancellationToken>())
            .Returns(false);

        var summary = await RunAsync(
            accountValidationService,
            new AccountKey("KR", "puuid-promoted"),
            new AccountKey("KR", "puuid-nothing-to-promote"));

        summary.Should().BeOfType<MatchIngestionSummary>();
        var ingestion = (MatchIngestionSummary)summary!;
        ingestion.AccountsProcessed.Should().Be(2);
        ingestion.AccountsValidated.Should().Be(1, "only one account had a candidate to promote");
        ingestion.Errors.Should().Be(0);
    }

    [Fact]
    public async Task RunCoreAsync_DoesNotCountAnAccountWhoseIngestionThrew()
    {
        var accountValidationService = Substitute.For<IAccountValidationService>();
        accountValidationService.ValidateAsync(Arg.Any<AccountKey>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var snapshotWriter = Substitute.For<IMatchSnapshotWriter>();
        snapshotWriter.IngestSnapshotsAsync(
                Arg.Any<IDataSession>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<Core.Lol.Identifiers.RegionalRoute>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<SnapshotIngestionResult>(new InvalidOperationException("ingest boom")));

        var summary = await RunAsync(
            accountValidationService,
            [new AccountKey("KR", "puuid-fails")],
            snapshotWriter);

        var ingestion = (MatchIngestionSummary)summary!;
        ingestion.Errors.Should().Be(1);
        ingestion.AccountsProcessed.Should().Be(0);
        ingestion.AccountsValidated.Should().Be(0, "the account was reverted, not validated");
    }

    private static Task<IProcessRunSummary?> RunAsync(
        IAccountValidationService accountValidationService,
        params AccountKey[] claimed)
        => RunAsync(accountValidationService, claimed, SucceedingSnapshotWriter());

    private static async Task<IProcessRunSummary?> RunAsync(
        IAccountValidationService accountValidationService,
        IReadOnlyCollection<AccountKey> claimed,
        IMatchSnapshotWriter snapshotWriter)
    {
        var sessionFactory = Substitute.For<IDataSessionFactory>();
        var session = Substitute.For<IDataSession>();
        session.BeginTransactionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Substitute.For<IDbContextTransaction>()));
        sessionFactory.CreateAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(session));

        var matchClaimService = Substitute.For<IMatchClaimService>();
        matchClaimService.ClaimAsync(
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<int>(),
                Arg.Any<double>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(claimed.ToList()));

        var process = new MatchIngestionProcess(
            NullLogger<MatchIngestionProcess>.Instance,
            sessionFactory,
            matchClaimService,
            snapshotWriter,
            Substitute.For<ITimelineIngestionService>(),
            accountValidationService,
            Microsoft.Extensions.Options.Options.Create(new MatchIngestionOptions
            {
                Platforms = ["KR"],
                BatchSize = claimed.Count
            }));

        return await process.RunCoreAsync(CancellationToken.None);
    }

    private static IMatchSnapshotWriter SucceedingSnapshotWriter()
    {
        var snapshotWriter = Substitute.For<IMatchSnapshotWriter>();
        snapshotWriter.IngestSnapshotsAsync(
                Arg.Any<IDataSession>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<Core.Lol.Identifiers.RegionalRoute>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SnapshotIngestionResult([], [], 0, 0)));
        return snapshotWriter;
    }
}
