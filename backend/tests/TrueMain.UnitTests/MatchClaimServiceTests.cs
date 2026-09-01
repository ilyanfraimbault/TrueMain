using Data.Entities;
using Data.Repositories;
using AwesomeAssertions;
using Ingestor.Processes.Components.Coverage;
using Ingestor.Processes.Components.MatchIngestion;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TrueMain.UnitTests.Fixtures;

namespace TrueMain.UnitTests;

public sealed class MatchClaimServiceTests
{
    [Fact]
    public async Task ClaimAsync_PassesLeaseToRepositoryAndUpdatesCandidateStatus()
    {
        var lease = TimeSpan.FromMinutes(30);
        var nowUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var timeProvider = new FixedTimeProvider(nowUtc);

        var sessionFactory = Substitute.For<IDataSessionFactory>();
        var session = Substitute.For<IDataSession>();
        var transaction = Substitute.For<IDbContextTransaction>();

        var riotAccounts = Substitute.For<IRiotAccountRepository>();
        var mainCandidates = Substitute.For<IMainCandidateRepository>();

        var claimedAccounts = new List<AccountKey> { new("KR", "puuid-1") };

        riotAccounts
            .ClaimAccountsForMatchIngestAtomicallyAsync(
                Arg.Any<IReadOnlyDictionary<string, int>>(),
                Arg.Any<int>(),
                Arg.Any<double>(),
                Arg.Any<DateTime>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(claimedAccounts);

        mainCandidates
            .SetStatusForAccountsAsync(
                Arg.Any<IReadOnlyCollection<AccountKey>>(),
                MainCandidateStatus.Queued,
                MainCandidateStatus.Processing,
                Arg.Any<CancellationToken>())
            .Returns(claimedAccounts);

        session.RiotAccounts.Returns(riotAccounts);
        session.MainCandidates.Returns(mainCandidates);
        session.BeginTransactionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(transaction));
        sessionFactory.CreateAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(session));

        var coverageProvider = Substitute.For<IChampionCoverageProvider>();
        coverageProvider.GetSnapshotAsync(Arg.Any<IDataSession>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ChampionCoverageSnapshot.Empty));

        var service = new MatchClaimService(
            sessionFactory, coverageProvider, timeProvider, NullLogger<MatchClaimService>.Instance);

        var result = await service.ClaimAsync(new[] { "KR" }, 10, 0.7, lease, CancellationToken.None);

        result.Should().ContainSingle().Which.Should().Be(new AccountKey("KR", "puuid-1"));

        // The single configured platform gets the whole batch: a neutral snapshot splits
        // evenly, and an even split over one platform is the batch (#1150).
        await riotAccounts.Received(1).ClaimAccountsForMatchIngestAtomicallyAsync(
            Arg.Is<IReadOnlyDictionary<string, int>>(quotas => quotas["KR"] == 10),
            10,
            0.7,
            nowUtc,
            lease,
            Arg.Any<CancellationToken>());

        // One set-based transition for the whole batch rather than one statement per
        // claimed account (#858, #1229).
        await mainCandidates.Received(1).SetStatusForAccountsAsync(
            Arg.Is<IReadOnlyCollection<AccountKey>>(batch => batch.Single() == new AccountKey("KR", "puuid-1")),
            MainCandidateStatus.Queued,
            MainCandidateStatus.Processing,
            Arg.Any<CancellationToken>());
    }
}
