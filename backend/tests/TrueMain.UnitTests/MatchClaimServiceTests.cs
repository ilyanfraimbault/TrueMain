using Data.Entities;
using Data.Repositories;
using AwesomeAssertions;
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
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<int>(),
                Arg.Any<DateTime>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(claimedAccounts);

        mainCandidates
            .SetStatusForAccountAsync("KR", "puuid-1", MainCandidateStatus.Queued, MainCandidateStatus.Processing, Arg.Any<CancellationToken>())
            .Returns(1);

        session.RiotAccounts.Returns(riotAccounts);
        session.MainCandidates.Returns(mainCandidates);
        session.BeginTransactionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(transaction));
        sessionFactory.CreateAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(session));

        var service = new MatchClaimService(sessionFactory, timeProvider, NullLogger<MatchClaimService>.Instance);

        var result = await service.ClaimAsync(new[] { "KR" }, 10, lease, CancellationToken.None);

        result.Should().ContainSingle().Which.Should().Be(new AccountKey("KR", "puuid-1"));

        await riotAccounts.Received(1).ClaimAccountsForMatchIngestAtomicallyAsync(
            Arg.Any<IReadOnlyCollection<string>>(),
            10,
            nowUtc,
            lease,
            Arg.Any<CancellationToken>());

        await mainCandidates.Received(1).SetStatusForAccountAsync(
            "KR",
            "puuid-1",
            MainCandidateStatus.Queued,
            MainCandidateStatus.Processing,
            Arg.Any<CancellationToken>());
    }
}
