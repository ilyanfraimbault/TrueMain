using AwesomeAssertions;
using Core.Lol.Identifiers;
using Data.Entities;
using Data.Ops.Mongo;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.Discovery;
using Ingestor.Processes.Summaries;
using Ingestor.Ranking;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TrueMain.UnitTests.Fixtures;

namespace TrueMain.UnitTests;

/// <summary>
/// The second half of the Discovery saving in #1358: a candidate whose masteries were read
/// within the freshness window is re-read for nothing — the crawl's budget belongs to accounts
/// we have never seen.
/// </summary>
public sealed class DiscoveryMasterySkipTests
{
    private static readonly DateTime NowUtc = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RunCoreAsync_SkipsTheMasteryCall_ForACandidateSeenWithinTheFreshnessWindow()
    {
        var harness = new Harness(masteryFreshPuuids: ["puuid-fresh"]);

        var summary = await harness.RunAsync();

        summary.Platforms.Should().ContainSingle().Which.MasteryCallsSkipped.Should().Be(1);

        await harness.PlatformClient.DidNotReceive().GetChampionMasteriesAsync(
            Arg.Any<PlatformRoute>(), "puuid-fresh", Arg.Any<CancellationToken>());
        await harness.PlatformClient.Received(1).GetChampionMasteriesAsync(
            Arg.Any<PlatformRoute>(), "puuid-stale", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCoreAsync_CountsTheSkippedAccount_AsProcessed()
    {
        // A skipped mastery call is not a skipped account: the profile upsert and the rank
        // snapshot still ran for it, so accountsProcessed must keep counting it or the
        // per-run throughput reads as a regression it is not.
        var harness = new Harness(masteryFreshPuuids: ["puuid-fresh"]);

        var summary = await harness.RunAsync();

        summary.Platforms.Should().ContainSingle().Which.AccountsProcessed.Should().Be(2);
    }

    [Fact]
    public async Task RunCoreAsync_CallsMasteryForEveryAccount_WhenNothingIsFresh()
    {
        var harness = new Harness(masteryFreshPuuids: []);

        var summary = await harness.RunAsync();

        summary.Platforms.Should().ContainSingle().Which.MasteryCallsSkipped.Should().Be(0);
        await harness.PlatformClient.Received(2).GetChampionMasteriesAsync(
            Arg.Any<PlatformRoute>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private sealed class Harness
    {
        public IRiotPlatformClient PlatformClient { get; } = Substitute.For<IRiotPlatformClient>();

        private readonly DiscoveryProcess _process;

        public Harness(string[] masteryFreshPuuids)
        {
            PlatformClient.GetChampionMasteriesAsync(
                    Arg.Any<PlatformRoute>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new List<RiotChampionMasteryDto>()));

            var accounts = Substitute.For<IRiotAccountRepository>();
            accounts.GetProfileFreshPuuidsAsync(
                    Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new HashSet<string>(StringComparer.Ordinal)));
            accounts.GetByKeysAsync(Arg.Any<IReadOnlyCollection<AccountKey>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new Dictionary<AccountKey, RiotAccount>()));

            var candidates = Substitute.For<IMainCandidateRepository>();
            candidates.GetPuuidsWithCandidatesSeenSinceAsync(
                    Arg.Any<string>(),
                    Arg.Any<IReadOnlyCollection<string>>(),
                    Arg.Any<DateTime>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new HashSet<string>(masteryFreshPuuids, StringComparer.Ordinal)));

            var rankSnapshots = Substitute.For<IRankSnapshotRepository>();
            rankSnapshots.GetLatestForAccountsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new Dictionary<Guid, RankSnapshot>()));

            var session = Substitute.For<IDataSession>();
            session.RiotAccounts.Returns(accounts);
            session.MainCandidates.Returns(candidates);
            session.RankSnapshots.Returns(rankSnapshots);
            session.DiscoveryCursors.Returns(Substitute.For<IDiscoveryCursorRepository>());
            session.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(0));

            var sessionFactory = Substitute.For<IDataSessionFactory>();
            sessionFactory.CreateAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(session));

            var ladder = Substitute.For<ILadderDiscoveryService>();
            ladder.DiscoverSummonersAsync(
                    Arg.Any<PlatformRoute>(),
                    Arg.Any<DiscoveryOptions>(),
                    Arg.Any<int>(),
                    Arg.Any<ProfileFreshnessProbe>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new LadderDiscoveryResult(
                    [
                        new DiscoveredSummoner(new RiotSummonerDto { Puuid = "puuid-fresh" }, Rank: null),
                        new DiscoveredSummoner(new RiotSummonerDto { Puuid = "puuid-stale" }, Rank: null)
                    ],
                    LadderSize: 2,
                    AppliedOffset: 0,
                    ProfileCallsSkipped: 0)));

            var accountUpsert = Substitute.For<IAccountUpsertService>();
            accountUpsert.UpsertAsync(
                    Arg.Any<IDataSession>(),
                    Arg.Any<PlatformRoute>(),
                    Arg.Any<RiotSummonerDto>(),
                    Arg.Any<DateTime>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<bool>())
                .Returns(call => Task.FromResult(new AccountUpsertResult(
                    IsNew: false,
                    Account: new RiotAccount
                    {
                        Id = Guid.NewGuid(),
                        Puuid = call.ArgAt<RiotSummonerDto>(2).Puuid,
                        PlatformId = "KR"
                    })));

            var candidateUpsert = Substitute.For<ICandidateUpsertService>();
            candidateUpsert.UpsertAsync(
                    Arg.Any<IDataSession>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<IReadOnlyCollection<RiotChampionMasteryDto>>(),
                    Arg.Any<DiscoveryOptions>(),
                    Arg.Any<DateTime>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new CandidateUpsertResult(0, 0)));

            _process = new DiscoveryProcess(
                NullLogger<DiscoveryProcess>.Instance,
                PlatformClient,
                sessionFactory,
                ladder,
                accountUpsert,
                candidateUpsert,
                Substitute.For<IRankSnapshotWriter>(),
                Substitute.For<IProcessRunStore>(),
                new FixedTimeProvider(NowUtc),
                Microsoft.Extensions.Options.Options.Create(new DiscoveryOptions
                {
                    Platforms = ["KR"],
                    TierScope = ["Challenger"],
                    ProfileSyncFreshness = TimeSpan.FromDays(7)
                }));
        }

        public async Task<DiscoverySummary> RunAsync()
        {
            var summary = await _process.RunCoreAsync(CancellationToken.None);
            return summary.Should().BeOfType<DiscoverySummary>().Subject;
        }
    }
}
