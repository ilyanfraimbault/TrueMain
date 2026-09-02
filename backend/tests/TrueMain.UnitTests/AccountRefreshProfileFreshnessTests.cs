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
/// AccountRefresh spent ~15 k calls per three days rewriting game names that had not changed:
/// reaching the head of the queue was the only condition (#1358). This is the profile mirror of
/// the rank freshness gate the same process already had.
/// </summary>
public sealed class AccountRefreshProfileFreshnessTests
{
    private static readonly DateTime NowUtc = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RunCoreAsync_SkipsTheProfileCall_WhenTheStoredProfileIsFresh()
    {
        var account = NewAccount(lastProfileSyncAtUtc: NowUtc.AddDays(-1));
        var accountClient = Substitute.For<IRiotAccountClient>();

        var summary = await RunAsync(accountClient, account);

        summary.ProfileSkippedFresh.Should().Be(1);
        summary.ProfileUpdated.Should().Be(0);
        await accountClient.DidNotReceive().GetAccountByPuuidAsync(
            Arg.Any<string>(), Arg.Any<Core.Lol.Identifiers.RegionalRoute>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCoreAsync_StampsTheSkippedRow_SoItLeavesTheHeadOfTheQueue()
    {
        // Every bucket of GetAccountsForRefreshAsync drains oldest-UpdatedAtUtc-first, so a
        // skip that leaves the stamp alone re-selects the same account on every cycle — the
        // batch would then do nothing at all, for ever (#1223's failure mode, #1358's gate).
        var account = NewAccount(lastProfileSyncAtUtc: NowUtc.AddDays(-1));
        account.UpdatedAtUtc = NowUtc.AddDays(-30);

        await RunAsync(Substitute.For<IRiotAccountClient>(), account);

        account.UpdatedAtUtc.Should().Be(NowUtc);
        account.LastProfileSyncAtUtc.Should().Be(
            NowUtc.AddDays(-1),
            "no call was made, so the sync stamp must not be refreshed — the gate would never reopen");
    }

    [Fact]
    public async Task RunCoreAsync_StillRefreshesRank_ForAnAccountWhoseProfileWasSkipped()
    {
        var account = NewAccount(lastProfileSyncAtUtc: NowUtc.AddDays(-1));
        var platformClient = Substitute.For<IRiotPlatformClient>();
        platformClient.GetLeagueEntriesByPuuidAsync(
                Arg.Any<Core.Lol.Identifiers.PlatformRoute>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RiotLeagueEntryByPuuidDto>()));

        await RunAsync(Substitute.For<IRiotAccountClient>(), platformClient, account);

        await platformClient.Received(1).GetLeagueEntriesByPuuidAsync(
            Arg.Any<Core.Lol.Identifiers.PlatformRoute>(), account.Puuid, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCoreAsync_NeverSkips_WhenTheIdentityIsStillIncomplete()
    {
        // account-v1 is the only writer of GameName/TagLine, and draining that backlog is
        // exactly what the selection's identity buckets exist for.
        var account = NewAccount(lastProfileSyncAtUtc: NowUtc.AddDays(-1));
        account.TagLine = null;

        var accountClient = Substitute.For<IRiotAccountClient>();
        accountClient.GetAccountByPuuidAsync(
                Arg.Any<string>(), Arg.Any<Core.Lol.Identifiers.RegionalRoute>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RiotAccountDto { Puuid = account.Puuid, GameName = "Solo", TagLine = "KR1" }));

        var summary = await RunAsync(accountClient, account);

        summary.ProfileSkippedFresh.Should().Be(0);
        summary.ProfileUpdated.Should().Be(1);
    }

    [Fact]
    public async Task RunCoreAsync_RefreshesTheProfile_WhenTheSyncIsOlderThanTheThreshold()
    {
        var account = NewAccount(lastProfileSyncAtUtc: NowUtc.AddDays(-30));

        var accountClient = Substitute.For<IRiotAccountClient>();
        accountClient.GetAccountByPuuidAsync(
                Arg.Any<string>(), Arg.Any<Core.Lol.Identifiers.RegionalRoute>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RiotAccountDto { Puuid = account.Puuid, GameName = "Renamed", TagLine = "KR1" }));

        var summary = await RunAsync(accountClient, account);

        summary.ProfileSkippedFresh.Should().Be(0);
        summary.ProfileUpdated.Should().Be(1);
        account.GameName.Should().Be("Renamed");
    }

    private static RiotAccount NewAccount(DateTime? lastProfileSyncAtUtc) => new()
    {
        Id = Guid.NewGuid(),
        Puuid = "puuid-a",
        PlatformId = "KR",
        GameName = "Solo",
        TagLine = "KR1",
        CreatedAtUtc = NowUtc.AddDays(-30),
        UpdatedAtUtc = NowUtc.AddDays(-1),
        LastProfileSyncAtUtc = lastProfileSyncAtUtc
    };

    private static Task<AccountRefreshSummary> RunAsync(
        IRiotAccountClient accountClient,
        params RiotAccount[] accounts)
    {
        var platformClient = Substitute.For<IRiotPlatformClient>();
        platformClient.GetLeagueEntriesByPuuidAsync(
                Arg.Any<Core.Lol.Identifiers.PlatformRoute>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<RiotLeagueEntryByPuuidDto>()));

        return RunAsync(accountClient, platformClient, accounts);
    }

    private static async Task<AccountRefreshSummary> RunAsync(
        IRiotAccountClient accountClient,
        IRiotPlatformClient platformClient,
        params RiotAccount[] accounts)
    {
        var riotAccounts = Substitute.For<IRiotAccountRepository>();
        riotAccounts.GetAccountsForRefreshAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(accounts.ToList()));
        riotAccounts.GetByKeysAsync(Arg.Any<IReadOnlyCollection<AccountKey>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(accounts.ToDictionary(
                account => new AccountKey(account.PlatformId, account.Puuid),
                account => account)));

        var rankSnapshots = Substitute.For<IRankSnapshotRepository>();
        rankSnapshots.GetLatestForAccountsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new Dictionary<Guid, RankSnapshot>()));

        var session = Substitute.For<IDataSession>();
        session.RiotAccounts.Returns(riotAccounts);
        session.RankSnapshots.Returns(rankSnapshots);
        session.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(0));

        var sessionFactory = Substitute.For<IDataSessionFactory>();
        sessionFactory.CreateAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(session));

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
