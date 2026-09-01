using AwesomeAssertions;
using Core.Lol.Identifiers;
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
/// The budget, rotation and cursor guarantees of the ladder sync (#1312): the whole point of
/// reading the ladder instead of one account at a time is that the run cost is bounded and the
/// sweep keeps moving, so those are the properties worth pinning.
/// </summary>
public sealed class LadderSyncProcessTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RunCoreAsync_ReadsEveryApexLadderWithoutSpendingThePaginatedBudget()
    {
        var client = new RecordingPlatformClient();
        var harness = new Harness(client);

        var summary = await harness.RunAsync(
            new LadderSyncOptions
            {
                Platforms = ["KR", "EUW1"],
                TierScope = ["Challenger", "GM", "Master"],
                MaxRequestsPerRun = 0
            });

        // Three tiers x two platforms, and none of it charged to the (here zero) budget.
        summary.ApexCalls.Should().Be(6);
        summary.PagedCalls.Should().Be(0);
        client.ApexCalls.Should().Equal(
            "KR/CHALLENGER", "KR/GRANDMASTER", "KR/MASTER",
            "EUW1/CHALLENGER", "EUW1/GRANDMASTER", "EUW1/MASTER");
    }

    [Fact]
    public async Task RunCoreAsync_StopsAtTheRequestBudgetAndSpreadsItAcrossPlatforms()
    {
        var client = new RecordingPlatformClient { PageSize = 3 };
        var harness = new Harness(client);

        var summary = await harness.RunAsync(
            new LadderSyncOptions
            {
                Platforms = ["KR", "EUW1"],
                TierScope = ["Diamond"],
                MaxRequestsPerRun = 4
            });

        summary.PagedCalls.Should().Be(4);

        // Round-robin, not platform-by-platform: draining KR first would leave EUW1 at page 1
        // forever once the budget is the binding constraint, which is the region-blind
        // allocation #1149/#1150 were about.
        client.PagedCalls.Select(call => call.Platform).Should().Equal("KR", "EUW1", "KR", "EUW1");
        client.PagedCalls.Select(call => call.Page).Should().Equal(1, 1, 2, 2);
    }

    [Fact]
    public async Task RunCoreAsync_AdvancesToTheNextDivisionOnAnEmptyPage()
    {
        // Riot answers a page past the end of a division with an empty array; that is the
        // sweep's only stop condition, so it must move to the next slot and reset to page 1.
        var client = new RecordingPlatformClient { PagesPerDivision = 1 };
        var harness = new Harness(client);

        await harness.RunAsync(
            new LadderSyncOptions
            {
                Platforms = ["KR"],
                TierScope = ["Diamond"],
                MaxRequestsPerRun = 3
            });

        client.PagedCalls.Select(call => $"{call.Division}:{call.Page}")
            .Should().Equal("I:1", "I:2", "II:1");

        harness.LastCursor.Should().Be(("KR", "DIAMOND", "II", 2));
    }

    [Fact]
    public async Task RunCoreAsync_AdvancesTheCursorPastAPageThatThrows()
    {
        // The cursor is persisted before the fetch for exactly this reason: a page that fails
        // deterministically would otherwise pin the sweep on it forever (the #486 lesson).
        var client = new RecordingPlatformClient { ThrowOnPage = 1 };
        var harness = new Harness(client);

        var summary = await harness.RunAsync(
            new LadderSyncOptions
            {
                Platforms = ["KR"],
                TierScope = ["Diamond"],
                MaxRequestsPerRun = 1
            });

        summary.FailedCalls.Should().Be(1);
        summary.PagedCalls.Should().Be(0);
        harness.LastCursor.Should().Be(("KR", "DIAMOND", "I", 2), "the failed page must not be retried forever");
    }

    [Fact]
    public async Task RunCoreAsync_WritesSnapshotsOnlyForAccountsWeAlreadyTrack()
    {
        // The sweep must never seed accounts: at three regions of Emerald that would be
        // millions of rows, and discovery is a different process's job.
        var client = new RecordingPlatformClient { PageSize = 3, PagesPerDivision = 1 };
        var known = NewAccount("KR", "puuid-1");
        var harness = new Harness(client, known);

        var summary = await harness.RunAsync(
            new LadderSyncOptions
            {
                Platforms = ["KR"],
                TierScope = ["Diamond"],
                MaxRequestsPerRun = 1
            });

        summary.EntriesFetched.Should().Be(3);
        summary.AccountsMatched.Should().Be(1, "the other two entries belong to players we do not track");
        summary.Tiers.Should().ContainSingle().Which.Should().Be(new LadderSyncTierSummary("DIAMOND", 3));
    }

    private static RiotAccount NewAccount(string platformId, string puuid)
        => new()
        {
            Id = Guid.NewGuid(),
            Puuid = puuid,
            PlatformId = platformId,
            GameName = "Known",
            TagLine = "EUW",
            CreatedAtUtc = NowUtc.AddDays(-30),
            UpdatedAtUtc = NowUtc.AddDays(-1)
        };

    private sealed class Harness
    {
        private readonly RecordingPlatformClient _client;
        private readonly IReadOnlyList<RiotAccount> _accounts;

        public Harness(RecordingPlatformClient client, params RiotAccount[] accounts)
        {
            _client = client;
            _accounts = accounts;
        }

        public (string Platform, string Tier, string Division, int Page)? LastCursor { get; private set; }

        public async Task<LadderSyncSummary> RunAsync(LadderSyncOptions options)
        {
            var riotAccounts = Substitute.For<IRiotAccountRepository>();
            riotAccounts.GetByKeysAsync(Arg.Any<IReadOnlyCollection<AccountKey>>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var requested = call.Arg<IReadOnlyCollection<AccountKey>>();
                    return Task.FromResult(_accounts
                        .Where(account => requested.Contains(new AccountKey(account.PlatformId, account.Puuid)))
                        .ToDictionary(account => new AccountKey(account.PlatformId, account.Puuid), account => account));
                });

            var rankSnapshots = Substitute.For<IRankSnapshotRepository>();
            rankSnapshots.GetLatestForAccountsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new Dictionary<Guid, RankSnapshot>()));

            var cursors = Substitute.For<ILadderSyncCursorRepository>();
            cursors.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<LadderSyncCursor?>(null));
            cursors.UpsertAsync(
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
                    Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    LastCursor = (call.ArgAt<string>(0), call.ArgAt<string>(1), call.ArgAt<string>(2), call.ArgAt<int>(3));
                    return Task.CompletedTask;
                });

            var session = Substitute.For<IDataSession>();
            session.RiotAccounts.Returns(riotAccounts);
            session.RankSnapshots.Returns(rankSnapshots);
            session.LadderSyncCursors.Returns(cursors);
            session.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(0));

            var sessionFactory = Substitute.For<IDataSessionFactory>();
            sessionFactory.CreateAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(session));

            var writer = Substitute.For<IRankSnapshotWriter>();
            writer.Ingest(
                    Arg.Any<IDataSession>(), Arg.Any<RiotAccount>(), Arg.Any<RankSnapshotInput>(),
                    Arg.Any<RankSnapshot?>(), Arg.Any<DateTime>())
                .Returns(RankSnapshotOutcome.Inserted);

            var process = new LadderSyncProcess(
                NullLogger<LadderSyncProcess>.Instance,
                _client,
                sessionFactory,
                writer,
                new FixedTimeProvider(NowUtc),
                Microsoft.Extensions.Options.Options.Create(options));

            var summary = await process.RunCoreAsync(CancellationToken.None);
            return summary.Should().BeOfType<LadderSyncSummary>().Subject;
        }
    }

    /// <summary>
    /// A ladder that answers every request, recording what was asked. <see cref="PagesPerDivision"/>
    /// bounds each division so the empty-page stop condition can be exercised.
    /// </summary>
    private sealed class RecordingPlatformClient : IRiotPlatformClient
    {
        public List<string> ApexCalls { get; } = [];

        public List<(string Platform, string Tier, string Division, int Page)> PagedCalls { get; } = [];

        public int PageSize { get; init; } = 2;

        public int PagesPerDivision { get; init; } = int.MaxValue;

        public int? ThrowOnPage { get; init; }

        public Task<RiotLeagueListDto> GetChallengerLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct)
            => ApexAsync(platform, "CHALLENGER");

        public Task<RiotLeagueListDto> GetGrandmasterLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct)
            => ApexAsync(platform, "GRANDMASTER");

        public Task<RiotLeagueListDto> GetMasterLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct)
            => ApexAsync(platform, "MASTER");

        public Task<List<RiotLeagueDivisionEntryDto>> GetLeagueEntriesAsync(
            PlatformRoute platform,
            string queue,
            string tier,
            string division,
            int page,
            CancellationToken ct)
        {
            if (ThrowOnPage == page)
            {
                return Task.FromException<List<RiotLeagueDivisionEntryDto>>(new HttpRequestException("simulated ladder outage"));
            }

            PagedCalls.Add((platform.ToString(), tier, division, page));

            var entries = page > PagesPerDivision
                ? []
                : Enumerable.Range(1, PageSize)
                    .Select(index => new RiotLeagueDivisionEntryDto
                    {
                        Puuid = $"puuid-{index}",
                        Tier = tier,
                        Rank = division,
                        LeaguePoints = 50,
                        Wins = 10,
                        Losses = 5
                    })
                    .ToList();

            return Task.FromResult(entries);
        }

        public Task<RiotSummonerDto> GetSummonerAsync(PlatformRoute platform, string summonerId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RiotSummonerDto> GetSummonerByPuuidAsync(PlatformRoute platform, string puuid, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<List<RiotChampionMasteryDto>> GetChampionMasteriesAsync(PlatformRoute platform, string puuid, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<List<RiotLeagueEntryByPuuidDto>> GetLeagueEntriesByPuuidAsync(PlatformRoute platform, string puuid, CancellationToken ct)
            => throw new NotSupportedException();

        private Task<RiotLeagueListDto> ApexAsync(PlatformRoute platform, string tier)
        {
            ApexCalls.Add($"{platform}/{tier}");
            return Task.FromResult(new RiotLeagueListDto { Tier = tier, Entries = [] });
        }
    }
}
