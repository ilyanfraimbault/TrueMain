using AwesomeAssertions;
using Data.Entities;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes.Components.Discovery;
using NSubstitute;

namespace TrueMain.UnitTests;

public sealed class ParticipantHarvestServiceTests
{
    private static readonly DateTime Now = new(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HarvestAsync_InsertsHarvestCandidateAndMinimalAccount_ForUnknownPuuid()
    {
        var harness = new Harness();
        harness.SetRows(new HarvestedCandidateRow("KR", "puuid-new", 22, 8, 5, Now.AddDays(-1)));

        var result = await harness.RunAsync();

        result.CandidatesInserted.Should().Be(1);
        result.CandidatesUpdated.Should().Be(0);
        result.AccountsCreated.Should().Be(1);

        harness.AddedCandidates.Should().ContainSingle();
        var candidate = harness.AddedCandidates.Single();
        candidate.Source.Should().Be(MainCandidateSource.Harvest);
        candidate.Status.Should().Be(MainCandidateStatus.New);
        candidate.ObservedGames.Should().Be(8);
        candidate.ObservedWins.Should().Be(5);
        candidate.LastPlayTimeUtc.Should().Be(Now.AddDays(-1));

        harness.AddedAccounts.Should().ContainSingle();
        var account = harness.AddedAccounts.Single();
        account.Puuid.Should().Be("puuid-new");
        account.PlatformId.Should().Be("KR");
        account.GameName.Should().BeEmpty();
        account.MatchIngestStatus.Should().Be(MatchIngestStatus.Idle);
    }

    [Fact]
    public async Task HarvestAsync_RefreshesObservedStats_WithoutDuplicating_ForExistingHarvestCandidate()
    {
        var harness = new Harness();
        var existing = new MainCandidate
        {
            PlatformId = "KR",
            Puuid = "puuid-known",
            ChampionId = 22,
            Source = MainCandidateSource.Harvest,
            ObservedGames = 3,
            ObservedWins = 1,
            LastPlayTimeUtc = Now.AddDays(-5),
            Status = MainCandidateStatus.Scored
        };
        harness.ExistingCandidate = existing;
        harness.ExistingAccountPuuids.Add("puuid-known");
        harness.SetRows(new HarvestedCandidateRow("KR", "puuid-known", 22, 11, 7, Now.AddHours(-2)));

        var result = await harness.RunAsync();

        result.CandidatesInserted.Should().Be(0);
        result.CandidatesUpdated.Should().Be(1);
        result.AccountsCreated.Should().Be(0);

        harness.AddedCandidates.Should().BeEmpty();
        existing.ObservedGames.Should().Be(11);
        existing.ObservedWins.Should().Be(7);
        existing.LastPlayTimeUtc.Should().Be(Now.AddHours(-2));
        // Status must not be reset by a refresh.
        existing.Status.Should().Be(MainCandidateStatus.Scored);
    }

    [Fact]
    public async Task HarvestAsync_DoesNotClobberLadderRecency_WhenRefreshingNonHarvestCandidate()
    {
        var harness = new Harness();
        var ladderLastPlay = Now.AddDays(-3);
        var existing = new MainCandidate
        {
            PlatformId = "KR",
            Puuid = "puuid-ladder",
            ChampionId = 22,
            Source = MainCandidateSource.Ladder,
            ChampionRankInMasteryTop = 1,
            ChampionPoints = 500_000,
            LastPlayTimeUtc = ladderLastPlay,
            Status = MainCandidateStatus.Scored
        };
        harness.ExistingCandidate = existing;
        harness.ExistingAccountPuuids.Add("puuid-ladder");
        harness.SetRows(new HarvestedCandidateRow("KR", "puuid-ladder", 22, 9, 4, Now.AddHours(-1)));

        await harness.RunAsync();

        // Observed signal is enriched, but mastery recency/fields stay untouched.
        existing.ObservedGames.Should().Be(9);
        existing.ObservedWins.Should().Be(4);
        existing.LastPlayTimeUtc.Should().Be(ladderLastPlay);
        existing.Source.Should().Be(MainCandidateSource.Ladder);
    }

    [Fact]
    public async Task HarvestAsync_CreatesAccountOnce_ForMultipleChampionRowsOfSamePuuid()
    {
        var harness = new Harness();
        harness.SetRows(
            new HarvestedCandidateRow("KR", "puuid-multi", 22, 8, 5, Now.AddDays(-1)),
            new HarvestedCandidateRow("KR", "puuid-multi", 64, 6, 2, Now.AddDays(-2)));

        var result = await harness.RunAsync();

        result.CandidatesInserted.Should().Be(2);
        result.AccountsCreated.Should().Be(1);
        harness.AddedAccounts.Should().ContainSingle();
    }

    private sealed class Harness
    {
        private readonly IDataSession _session = Substitute.For<IDataSession>();
        private readonly IMatchParticipantRepository _participants = Substitute.For<IMatchParticipantRepository>();
        private readonly IRiotAccountRepository _accounts = Substitute.For<IRiotAccountRepository>();
        private readonly IMainCandidateRepository _candidates = Substitute.For<IMainCandidateRepository>();

        public List<MainCandidate> AddedCandidates { get; } = [];
        public List<RiotAccount> AddedAccounts { get; } = [];
        public MainCandidate? ExistingCandidate { get; set; }
        public HashSet<string> ExistingAccountPuuids { get; } = new(StringComparer.Ordinal);

        public Harness()
        {
            _session.MatchParticipants.Returns(_participants);
            _session.RiotAccounts.Returns(_accounts);
            _session.MainCandidates.Returns(_candidates);

            _accounts.GetByPuuidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(
                    ExistingAccountPuuids.Contains(call.Arg<string>())
                        ? new RiotAccount { Puuid = call.Arg<string>() }
                        : null));
            _accounts.When(a => a.Add(Arg.Any<RiotAccount>()))
                .Do(call => AddedAccounts.Add(call.Arg<RiotAccount>()));

            _candidates.GetByPlatformPuuidAndChampionsAsync(
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<List<int>>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var championIds = call.ArgAt<List<int>>(2);
                    var match = ExistingCandidate is not null
                                && ExistingCandidate.Puuid == call.ArgAt<string>(1)
                                && championIds.Contains(ExistingCandidate.ChampionId)
                        ? new List<MainCandidate> { ExistingCandidate }
                        : new List<MainCandidate>();
                    return Task.FromResult(match);
                });
            _candidates.When(c => c.Add(Arg.Any<MainCandidate>()))
                .Do(call => AddedCandidates.Add(call.Arg<MainCandidate>()));
        }

        public void SetRows(params HarvestedCandidateRow[] rows)
            => _participants.GetHarvestCandidatesAsync(
                    Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(rows.ToList()));

        public Task<HarvestResult> RunAsync()
            => new ParticipantHarvestService().HarvestAsync(
                _session,
                new HarvestOptions { Platforms = ["KR"], MinObservedGames = 5 },
                Now,
                CancellationToken.None);
    }
}
