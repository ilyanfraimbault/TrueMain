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
        harness.ExistingCandidates.Add(existing);
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
        // A Scored-but-unpromoted harvest candidate is reset to New so the refreshed
        // observed sample is re-scored on the same pass.
        existing.Status.Should().Be(MainCandidateStatus.New);
        existing.ScoredAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task HarvestAsync_DoesNotResetInFlightHarvestCandidate()
    {
        var harness = new Harness();
        var existing = new MainCandidate
        {
            PlatformId = "KR",
            Puuid = "puuid-queued",
            ChampionId = 22,
            Source = MainCandidateSource.Harvest,
            ObservedGames = 6,
            LastPlayTimeUtc = Now.AddDays(-2),
            Status = MainCandidateStatus.Queued
        };
        harness.ExistingCandidates.Add(existing);
        harness.ExistingAccountPuuids.Add("puuid-queued");
        harness.SetRows(new HarvestedCandidateRow("KR", "puuid-queued", 22, 12, 8, Now.AddHours(-1)));

        await harness.RunAsync();

        // In-flight candidates keep their place; only the observed stats refresh.
        existing.Status.Should().Be(MainCandidateStatus.Queued);
        existing.ObservedGames.Should().Be(12);
    }

    [Fact]
    public async Task HarvestAsync_LeavesNonHarvestCandidateUntouched()
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
            ObservedGames = 0,
            ObservedWins = 0,
            LastPlayTimeUtc = ladderLastPlay,
            Status = MainCandidateStatus.Scored
        };
        harness.ExistingCandidates.Add(existing);
        harness.ExistingAccountPuuids.Add("puuid-ladder");
        harness.SetRows(new HarvestedCandidateRow("KR", "puuid-ladder", 22, 9, 4, Now.AddHours(-1)));

        var result = await harness.RunAsync();

        // Invariant: observed stats stay 0 outside Harvest, and mastery recency is untouched.
        result.CandidatesUpdated.Should().Be(0);
        existing.ObservedGames.Should().Be(0);
        existing.ObservedWins.Should().Be(0);
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

    [Fact]
    public async Task HarvestAsync_PassesLookbackCutoff_RelativeToNow()
    {
        var harness = new Harness();
        harness.SetRows();

        await harness.RunAsync(lookbackDays: 30);

        await harness.Participants.Received(1).GetHarvestCandidatesAsync(
            Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(),
            Now.AddDays(-30), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HarvestAsync_PassesEpochCutoff_WhenLookbackDisabled()
    {
        var harness = new Harness();
        harness.SetRows();

        await harness.RunAsync(lookbackDays: 0);

        await harness.Participants.Received(1).GetHarvestCandidatesAsync(
            Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(),
            DateTime.UnixEpoch, Arg.Any<CancellationToken>());
    }

    private sealed class Harness
    {
        private readonly IDataSession _session = Substitute.For<IDataSession>();
        private readonly IRiotAccountRepository _accounts = Substitute.For<IRiotAccountRepository>();
        private readonly IMainCandidateRepository _candidates = Substitute.For<IMainCandidateRepository>();

        public IMatchParticipantRepository Participants { get; } = Substitute.For<IMatchParticipantRepository>();
        public List<MainCandidate> AddedCandidates { get; } = [];
        public List<RiotAccount> AddedAccounts { get; } = [];
        public List<MainCandidate> ExistingCandidates { get; } = [];
        public HashSet<string> ExistingAccountPuuids { get; } = new(StringComparer.Ordinal);

        public Harness()
        {
            _session.MatchParticipants.Returns(Participants);
            _session.RiotAccounts.Returns(_accounts);
            _session.MainCandidates.Returns(_candidates);

            _accounts.GetExistingPuuidsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(new HashSet<string>(ExistingAccountPuuids, StringComparer.Ordinal)));
            _accounts.When(a => a.Add(Arg.Any<RiotAccount>()))
                .Do(call => AddedAccounts.Add(call.Arg<RiotAccount>()));

            _candidates.GetByPlatformsAndPuuidsAsync(
                    Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(ExistingCandidates.ToList()));
            _candidates.When(c => c.Add(Arg.Any<MainCandidate>()))
                .Do(call => AddedCandidates.Add(call.Arg<MainCandidate>()));
        }

        public void SetRows(params HarvestedCandidateRow[] rows)
            => Participants.GetHarvestCandidatesAsync(
                    Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(rows.ToList()));

        public Task<HarvestResult> RunAsync(int lookbackDays = 0)
            => new ParticipantHarvestService().HarvestAsync(
                _session,
                new HarvestOptions { Platforms = ["KR"], MinObservedGames = 5, LookbackDays = lookbackDays },
                Now,
                CancellationToken.None);
    }
}
