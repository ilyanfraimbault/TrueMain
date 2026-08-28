using AwesomeAssertions;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.MatchIngestion;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// A claimed account whose <c>platform_id</c> does not parse is an expected data
/// condition, not a fault (#1223). It used to be thrown, which routed it through
/// <see cref="IAccountValidationService.RevertAsync"/> — the path that deliberately does
/// not stamp <c>LastMatchIngestAtUtc</c> so a transient failure is retried at once. Since
/// claims are ordered never-ingested-first then oldest-ingested-first, the row then came
/// back at the head of every batch and consumed a slot on every cycle, forever.
/// </summary>
public sealed class MatchIngestionProcessUnknownPlatformTests
{
    [Fact]
    public async Task RunCoreAsync_WhenPlatformDoesNotParse_ReleasesTheClaimInsteadOfReverting()
    {
        var accountValidationService = Substitute.For<IAccountValidationService>();
        var process = CreateProcess(accountValidationService, new AccountKey("XX9", "puuid-corrupt"));

        var act = async () => await process.RunCoreAsync(CancellationToken.None);

        await act.Should().NotThrowAsync("an unparseable platform is a state, not an exception");

        await accountValidationService.Received(1).ReleaseUningestableAsync(
            Arg.Is<AccountKey>(key => key.Puuid == "puuid-corrupt"),
            Arg.Any<CancellationToken>());
        await accountValidationService.DidNotReceive().RevertAsync(
            Arg.Any<AccountKey>(), Arg.Any<CancellationToken>());
        await accountValidationService.DidNotReceive().ValidateAsync(
            Arg.Any<AccountKey>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCoreAsync_WhenOneAccountHasAnUnknownPlatform_KeepsIngestingTheRest()
    {
        // The bad row must cost its own slot only: the rest of the claimed batch still
        // has to go through ingestion.
        var accountValidationService = Substitute.For<IAccountValidationService>();
        var sessionFactory = Substitute.For<IDataSessionFactory>();
        // Any account that gets past the platform guard fails on the session, landing in
        // the ordinary revert path — proof it was actually attempted.
        sessionFactory.CreateAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<IDataSession>(new InvalidOperationException("ingest boom")));

        var process = CreateProcess(
            accountValidationService,
            sessionFactory,
            new AccountKey("XX9", "puuid-corrupt"),
            new AccountKey("KR", "puuid-valid"));

        await process.RunCoreAsync(CancellationToken.None);

        await accountValidationService.Received(1).ReleaseUningestableAsync(
            Arg.Is<AccountKey>(key => key.Puuid == "puuid-corrupt"),
            Arg.Any<CancellationToken>());
        await accountValidationService.Received(1).RevertAsync(
            Arg.Is<AccountKey>(key => key.Puuid == "puuid-valid"),
            Arg.Any<CancellationToken>());
    }

    private static MatchIngestionProcess CreateProcess(
        IAccountValidationService accountValidationService,
        params AccountKey[] claimed)
        => CreateProcess(accountValidationService, Substitute.For<IDataSessionFactory>(), claimed);

    private static MatchIngestionProcess CreateProcess(
        IAccountValidationService accountValidationService,
        IDataSessionFactory sessionFactory,
        params AccountKey[] claimed)
    {
        var matchClaimService = Substitute.For<IMatchClaimService>();
        matchClaimService.ClaimAsync(
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<int>(),
                Arg.Any<double>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(claimed.ToList()));

        return new MatchIngestionProcess(
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
    }
}
