using System.Net;
using AwesomeAssertions;
using Data.Entities;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Summaries;
using Ingestor.Ranking;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TrueMain.UnitTests.Fixtures;

namespace TrueMain.UnitTests;

/// <summary>
/// The two guards AccountRefresh was missing (#1223): the PUUID collision check that
/// only looked at the database and never at the batch it was part of, and the
/// invalid-platform path that skipped a row without moving it off the head of the
/// selection.
/// </summary>
public sealed class AccountRefreshProcessGuardTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RunCoreAsync_WhenTwoAccountsOfTheBatchRecoverToTheSamePuuid_KeepsOnlyOne()
    {
        // Both accounts 404 on by-puuid and both resolve, by Riot ID, to the same PUUID.
        // The database knows neither of them under that PUUID, so ExistsByPuuidAsync
        // answers false for both — nothing is persisted before the batch's single
        // SaveChangesAsync. Before #1223 both rows took the PUUID and the save violated
        // the unique index, failing the refresh of all 200 accounts in the batch.
        const string sharedPuuid = "recovered-shared-puuid";

        var first = NewAccount("stale-puuid-1", tagLine: "KR1");
        var second = NewAccount("stale-puuid-2", tagLine: "KR2");

        var accountClient = Substitute.For<IRiotAccountClient>();
        accountClient.GetAccountByPuuidAsync(Arg.Any<string>(), Arg.Any<Core.Lol.Identifiers.RegionalRoute>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<RiotAccountDto>(
                new HttpRequestException("gone", null, HttpStatusCode.NotFound)));
        accountClient.GetByRiotIdAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Core.Lol.Identifiers.RegionalRoute>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<RiotAccountDto?>(new RiotAccountDto
            {
                Puuid = sharedPuuid,
                GameName = "Twin",
                TagLine = "KR1"
            }));

        var summary = await RunAsync(accountClient, first, second);

        summary.ProfileRecovered.Should().Be(1, "exactly one row may take the recovered PUUID");
        summary.ProfileInvalidated.Should().Be(1, "the other is a stale duplicate, not a fresh account");

        new[] { first, second }.Count(account => account.Puuid == sharedPuuid)
            .Should().Be(1, "two rows holding the same PUUID is the unique-index violation itself");
        new[] { first, second }.Count(account => account.Status == RiotAccountStatus.Invalid)
            .Should().Be(1);
    }

    [Fact]
    public async Task RunCoreAsync_WhenRecoveredPuuidIsFreeInTheBatch_StillRecoversTheAccount()
    {
        // The guard above must not fire on an ordinary recovery: one account, one free
        // PUUID, nothing else in the batch claiming it.
        var account = NewAccount("stale-puuid", tagLine: "KR1");

        var accountClient = Substitute.For<IRiotAccountClient>();
        accountClient.GetAccountByPuuidAsync(Arg.Any<string>(), Arg.Any<Core.Lol.Identifiers.RegionalRoute>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<RiotAccountDto>(
                new HttpRequestException("gone", null, HttpStatusCode.NotFound)));
        accountClient.GetByRiotIdAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Core.Lol.Identifiers.RegionalRoute>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<RiotAccountDto?>(new RiotAccountDto
            {
                Puuid = "fresh-puuid",
                GameName = "Solo",
                TagLine = "KR1"
            }));

        var summary = await RunAsync(accountClient, account);

        summary.ProfileRecovered.Should().Be(1);
        summary.ProfileInvalidated.Should().Be(0);
        account.Puuid.Should().Be("fresh-puuid");
    }

    [Fact]
    public async Task RunCoreAsync_WhenPlatformDoesNotParse_StampsTheRowSoItLeavesTheHeadOfTheQueue()
    {
        // Every bucket of GetAccountsForRefreshAsync drains oldest-UpdatedAtUtc-first.
        // Skipping the row without touching that column parked it at the head of every
        // batch forever, burning a slot on each cycle (#1223).
        var stale = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var account = NewAccount("corrupt-platform-puuid", tagLine: "KR1", platformId: "XX9");
        account.UpdatedAtUtc = stale;

        var accountClient = Substitute.For<IRiotAccountClient>();

        var summary = await RunAsync(accountClient, account);

        summary.ProfileSkipped.Should().Be(1);
        account.UpdatedAtUtc.Should().Be(NowUtc, "the condition is permanent, so the row must move to the back");

        await accountClient.DidNotReceive().GetAccountByPuuidAsync(
            Arg.Any<string>(), Arg.Any<Core.Lol.Identifiers.RegionalRoute>(), Arg.Any<CancellationToken>());
    }

    private static RiotAccount NewAccount(
        string puuid,
        string tagLine,
        string platformId = "KR",
        string gameName = "Twin")
        => new()
        {
            Id = Guid.NewGuid(),
            Puuid = puuid,
            PlatformId = platformId,
            GameName = gameName,
            TagLine = tagLine,
            CreatedAtUtc = NowUtc.AddDays(-30),
            UpdatedAtUtc = NowUtc.AddDays(-1)
        };

    private static async Task<AccountRefreshSummary> RunAsync(
        IRiotAccountClient accountClient,
        params RiotAccount[] accounts)
    {
        var riotAccounts = Substitute.For<IRiotAccountRepository>();
        riotAccounts.GetAccountsForRefreshAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(accounts.ToList()));
        riotAccounts.GetByKeysAsync(Arg.Any<IReadOnlyCollection<AccountKey>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(accounts.ToDictionary(
                account => new AccountKey(account.PlatformId, account.Puuid),
                account => account)));
        // The database holds none of the recovered PUUIDs: the collision this test is
        // about is purely between two accounts of the same, not-yet-saved batch.
        riotAccounts.ExistsByPuuidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var rankSnapshots = Substitute.For<IRankSnapshotRepository>();
        rankSnapshots.GetLatestForAccountsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new Dictionary<Guid, RankSnapshot>()));

        var session = Substitute.For<IDataSession>();
        session.RiotAccounts.Returns(riotAccounts);
        session.RankSnapshots.Returns(rankSnapshots);
        session.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(0));

        var sessionFactory = Substitute.For<IDataSessionFactory>();
        sessionFactory.CreateAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(session));

        var platformClient = Substitute.For<IRiotPlatformClient>();
        platformClient.GetLeagueEntriesByPuuidAsync(
                Arg.Any<Core.Lol.Identifiers.PlatformRoute>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RiotLeagueEntryByPuuidDto>()));

        var process = new AccountRefreshProcess(
            NullLogger<AccountRefreshProcess>.Instance,
            accountClient,
            platformClient,
            sessionFactory,
            Substitute.For<IRankSnapshotWriter>(),
            new FixedTimeProvider(NowUtc),
            Microsoft.Extensions.Options.Options.Create(new AccountRefreshOptions()));

        var summary = await process.RunCoreAsync(CancellationToken.None);
        return summary.Should().BeOfType<AccountRefreshSummary>().Subject;
    }
}
