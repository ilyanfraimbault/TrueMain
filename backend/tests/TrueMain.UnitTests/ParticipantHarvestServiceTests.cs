using AwesomeAssertions;
using Data.Entities;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes.Components.Coverage;
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
        harness.SetRows(new HarvestedCandidateRow("KR", "puuid-new", 22, 8, 5, Now.AddDays(-1), IsKnownCandidate: false));

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
            Score = 42,
            ScoredAtUtc = Now.AddDays(-5),
            Status = MainCandidateStatus.Scored
        };
        harness.ExistingCandidates.Add(existing);
        harness.ExistingAccountPuuids.Add("puuid-known");
        harness.SetRows(new HarvestedCandidateRow("KR", "puuid-known", 22, 11, 7, Now.AddHours(-2), IsKnownCandidate: true));

        var result = await harness.RunAsync();

        result.CandidatesInserted.Should().Be(0);
        result.CandidatesUpdated.Should().Be(1);
        result.AccountsCreated.Should().Be(0);

        harness.AddedCandidates.Should().BeEmpty();
        existing.ObservedGames.Should().Be(11);
        existing.ObservedWins.Should().Be(7);
        existing.LastPlayTimeUtc.Should().Be(Now.AddHours(-2));
        // A Scored-but-unpromoted harvest candidate is reset to New so the refreshed
        // observed sample is re-scored on the same pass; the stale score is cleared.
        existing.Status.Should().Be(MainCandidateStatus.New);
        existing.ScoredAtUtc.Should().BeNull();
        existing.Score.Should().Be(0);
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
        harness.SetRows(new HarvestedCandidateRow("KR", "puuid-queued", 22, 12, 8, Now.AddHours(-1), IsKnownCandidate: true));

        await harness.RunAsync();

        // In-flight candidates keep their place; all three observed fields refresh together.
        existing.Status.Should().Be(MainCandidateStatus.Queued);
        existing.ObservedGames.Should().Be(12);
        existing.ObservedWins.Should().Be(8);
        existing.LastPlayTimeUtc.Should().Be(Now.AddHours(-1));
    }

    [Fact]
    public async Task HarvestAsync_DoesNotResetValidatedHarvestCandidate()
    {
        var harness = new Harness();
        var existing = new MainCandidate
        {
            PlatformId = "KR",
            Puuid = "puuid-validated",
            ChampionId = 22,
            Source = MainCandidateSource.Harvest,
            ObservedGames = 6,
            ObservedWins = 3,
            LastPlayTimeUtc = Now.AddDays(-2),
            ValidatedAtUtc = Now.AddDays(-1),
            Status = MainCandidateStatus.Validated
        };
        harness.ExistingCandidates.Add(existing);
        harness.ExistingAccountPuuids.Add("puuid-validated");
        harness.SetRows(new HarvestedCandidateRow("KR", "puuid-validated", 22, 12, 8, Now.AddHours(-1), IsKnownCandidate: true));

        await harness.RunAsync();

        // A validated main keeps its status; only the observed stats refresh.
        existing.Status.Should().Be(MainCandidateStatus.Validated);
        existing.ObservedGames.Should().Be(12);
        existing.ObservedWins.Should().Be(8);
        existing.LastPlayTimeUtc.Should().Be(Now.AddHours(-1));
    }

    [Fact]
    public async Task HarvestAsync_DoesNotResetProcessingHarvestCandidate()
    {
        var harness = new Harness();
        var existing = new MainCandidate
        {
            PlatformId = "KR",
            Puuid = "puuid-processing",
            ChampionId = 22,
            Source = MainCandidateSource.Harvest,
            ObservedGames = 6,
            ObservedWins = 3,
            LastPlayTimeUtc = Now.AddDays(-2),
            Status = MainCandidateStatus.Processing
        };
        harness.ExistingCandidates.Add(existing);
        harness.ExistingAccountPuuids.Add("puuid-processing");
        harness.SetRows(new HarvestedCandidateRow("KR", "puuid-processing", 22, 12, 8, Now.AddHours(-1), IsKnownCandidate: true));

        await harness.RunAsync();

        // Mid-ingestion candidates keep their place; only the observed stats refresh.
        existing.Status.Should().Be(MainCandidateStatus.Processing);
        existing.ObservedGames.Should().Be(12);
        existing.ObservedWins.Should().Be(8);
        existing.LastPlayTimeUtc.Should().Be(Now.AddHours(-1));
    }

    [Fact]
    public async Task HarvestAsync_DoesNotResurrectRejectedCandidate()
    {
        var harness = new Harness();
        var existing = new MainCandidate
        {
            PlatformId = "KR",
            Puuid = "puuid-rejected",
            ChampionId = 22,
            Source = MainCandidateSource.Harvest,
            ObservedGames = 5,
            LastPlayTimeUtc = Now.AddDays(-2),
            Status = MainCandidateStatus.Rejected
        };
        harness.ExistingCandidates.Add(existing);
        harness.ExistingAccountPuuids.Add("puuid-rejected");
        harness.SetRows(new HarvestedCandidateRow("KR", "puuid-rejected", 22, 80, 50, Now.AddHours(-1), IsKnownCandidate: true));

        await harness.RunAsync();

        // A rejection is a verdict from real history + MainAnalysis, not from this biased
        // sample, so a much larger observed count must not re-queue it. Stats still refresh.
        existing.Status.Should().Be(MainCandidateStatus.Rejected);
        existing.ObservedGames.Should().Be(80);
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
        harness.SetRows(new HarvestedCandidateRow("KR", "puuid-ladder", 22, 9, 4, Now.AddHours(-1), IsKnownCandidate: true));

        var result = await harness.RunAsync();

        // The query no longer returns pairs whose candidate is not harvest-owned (#495), so
        // this is the last line of defence: a ladder candidate created between the scan and
        // the write must still be left alone.
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
            new HarvestedCandidateRow("KR", "puuid-multi", 22, 8, 5, Now.AddDays(-1), IsKnownCandidate: false),
            new HarvestedCandidateRow("KR", "puuid-multi", 64, 6, 2, Now.AddDays(-2), IsKnownCandidate: false));

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

    [Fact]
    public async Task HarvestAsync_StillHarvestsNewPairs_WhenKnownPairsCouldFillTheWholeBudget()
    {
        var harness = new Harness();
        // The starvation scenario (#495): the four most-observed pairs are already
        // candidates and would fill a budget of 4 on their own, leaving nothing for the two
        // pairs that just crossed the observed-games gate.
        foreach (var known in new[] { ("known-a", 90), ("known-b", 80), ("known-c", 70), ("known-d", 60) })
        {
            harness.ExistingCandidates.Add(new MainCandidate
            {
                PlatformId = "KR",
                Puuid = known.Item1,
                ChampionId = 22,
                Source = MainCandidateSource.Harvest,
                ObservedGames = known.Item2 - 10,
                LastPlayTimeUtc = Now.AddDays(-3),
                Status = MainCandidateStatus.Queued
            });
            harness.ExistingAccountPuuids.Add(known.Item1);
        }

        harness.SetRows(
            new HarvestedCandidateRow("KR", "known-a", 22, 90, 50, Now.AddHours(-1), IsKnownCandidate: true),
            new HarvestedCandidateRow("KR", "known-b", 22, 80, 40, Now.AddHours(-2), IsKnownCandidate: true),
            new HarvestedCandidateRow("KR", "known-c", 22, 70, 35, Now.AddHours(-3), IsKnownCandidate: true),
            new HarvestedCandidateRow("KR", "known-d", 22, 60, 30, Now.AddHours(-4), IsKnownCandidate: true),
            new HarvestedCandidateRow("KR", "newcomer-a", 22, 6, 3, Now.AddHours(-5), IsKnownCandidate: false),
            new HarvestedCandidateRow("KR", "newcomer-b", 22, 5, 2, Now.AddHours(-6), IsKnownCandidate: false));

        var result = await harness.RunAsync(maxCandidatesPerRun: 4, newCandidateShare: 0.5);

        // Half the budget is reserved for new discovery, so both newcomers are harvested
        // even though every one of them ranks below every known pair.
        result.CandidatesInserted.Should().Be(2);
        harness.AddedCandidates.Select(candidate => candidate.Puuid)
            .Should().BeEquivalentTo(["newcomer-a", "newcomer-b"]);
        // The other half still refreshes, taking the most-observed known pairs first.
        result.CandidatesUpdated.Should().Be(2);
        harness.ExistingCandidates
            .Where(candidate => candidate.ObservedGames >= 80)
            .Select(candidate => candidate.Puuid)
            .Should().BeEquivalentTo(["known-a", "known-b"]);
    }

    [Fact]
    public async Task HarvestAsync_SpendsTheReservationOnRefreshes_WhenTooFewNewPairsQualify()
    {
        var harness = new Harness();
        foreach (var puuid in new[] { "known-a", "known-b", "known-c" })
        {
            harness.ExistingCandidates.Add(new MainCandidate
            {
                PlatformId = "KR",
                Puuid = puuid,
                ChampionId = 22,
                Source = MainCandidateSource.Harvest,
                LastPlayTimeUtc = Now.AddDays(-3),
                Status = MainCandidateStatus.Queued
            });
            harness.ExistingAccountPuuids.Add(puuid);
        }

        harness.SetRows(
            new HarvestedCandidateRow("KR", "known-a", 22, 90, 50, Now.AddHours(-1), IsKnownCandidate: true),
            new HarvestedCandidateRow("KR", "known-b", 22, 80, 40, Now.AddHours(-2), IsKnownCandidate: true),
            new HarvestedCandidateRow("KR", "known-c", 22, 70, 35, Now.AddHours(-3), IsKnownCandidate: true),
            new HarvestedCandidateRow("KR", "newcomer", 22, 6, 3, Now.AddHours(-5), IsKnownCandidate: false));

        var result = await harness.RunAsync(maxCandidatesPerRun: 4, newCandidateShare: 0.5);

        // The reservation is a floor, not a partition: only one new pair qualifies, so the
        // run spends the rest of the budget on refreshes instead of wasting the slots.
        result.CandidatesInserted.Should().Be(1);
        result.CandidatesUpdated.Should().Be(3);
        result.Coverage.IsBudgetBound.Should().BeFalse();
    }

    [Fact]
    public async Task HarvestAsync_ReportsWhatTheBudgetLeftBehind()
    {
        var harness = new Harness();
        // A pool far larger than the run's budget: 40 new and 100 known pairs qualified on
        // KR, but only three rows fit. Coverage must carry the real totals so the process
        // can log the shortfall instead of truncating silently.
        harness.SetBatch(new HarvestCandidateBatch(
            [
                new HarvestedCandidateRow("KR", "newcomer-a", 22, 9, 5, Now.AddHours(-1), IsKnownCandidate: false),
                new HarvestedCandidateRow("KR", "newcomer-b", 22, 8, 4, Now.AddHours(-2), IsKnownCandidate: false),
                new HarvestedCandidateRow("KR", "known-a", 22, 90, 50, Now.AddHours(-3), IsKnownCandidate: true)
            ],
            [new HarvestPlatformEligibility("KR", EligibleNew: 40, EligibleKnown: 100)]));

        var result = await harness.RunAsync(maxCandidatesPerRun: 2, newCandidateShare: 0.5);

        var coverage = result.Coverage;
        coverage.EligibleNew.Should().Be(40);
        coverage.SelectedNew.Should().Be(1);
        coverage.DroppedNew.Should().Be(39);
        coverage.EligibleKnown.Should().Be(100);
        coverage.SelectedKnown.Should().Be(1);
        coverage.DroppedKnown.Should().Be(99);
        coverage.IsBudgetBound.Should().BeTrue();
        coverage.Platforms.Should().ContainSingle();
        coverage.Platforms.Single().Should().BeEquivalentTo(
            new { PlatformId = "KR", EligibleNew = 40, SelectedNew = 1, EligibleKnown = 100, SelectedKnown = 1 });
    }

    [Fact]
    public async Task HarvestAsync_ReportsFullCoverage_WhenTheBudgetFitsTheWholePool()
    {
        var harness = new Harness();
        harness.SetRows(
            new HarvestedCandidateRow("KR", "newcomer", 22, 9, 5, Now.AddHours(-1), IsKnownCandidate: false));

        var result = await harness.RunAsync(maxCandidatesPerRun: 5000);

        result.Coverage.IsBudgetBound.Should().BeFalse();
        result.Coverage.DroppedNew.Should().Be(0);
        result.Coverage.DroppedKnown.Should().Be(0);
    }

    [Fact]
    public async Task HarvestAsync_PassesMaxCandidatesPerRun_AsThePerBucketCap()
    {
        var harness = new Harness();
        harness.SetRows();

        await harness.RunAsync(maxCandidatesPerRun: 777);

        // The repository caps each class on each platform; the run-wide budget is applied
        // afterwards, over the union, so the cap it receives is the full budget.
        await harness.Participants.Received(1).GetHarvestCandidatesAsync(
            Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<int>(), Arg.Any<int>(), 777,
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HarvestAsync_SplitsTheBudgetAcrossPlatforms_InsteadOfOrderingGlobally()
    {
        // The mechanism behind the imbalance (#1150): observed games come from the matches we
        // already ingested, so the densest observations are always the region we ingest most.
        // Ordering the union by observed games and cutting at the budget therefore handed that
        // region the whole budget — every EUW1 row here outranks every KR row.
        var harness = new Harness();
        harness.SetRows(
        [
            .. Enumerable.Range(1, 6).Select(i =>
                new HarvestedCandidateRow("EUW1", $"euw-{i}", 22, 100 + i, 50, Now, IsKnownCandidate: false)),
            .. Enumerable.Range(1, 6).Select(i =>
                new HarvestedCandidateRow("KR", $"kr-{i}", 22, 10 + i, 5, Now, IsKnownCandidate: false))
        ]);

        var result = await harness.RunAsync(maxCandidatesPerRun: 6, platforms: ["EUW1", "KR"]);

        result.CandidatesInserted.Should().Be(6);
        harness.AddedCandidates.Count(candidate => candidate.PlatformId == "KR").Should().Be(3);
        harness.AddedCandidates.Count(candidate => candidate.PlatformId == "EUW1").Should().Be(3);
    }

    [Fact]
    public async Task HarvestAsync_FavoursTheUnderCoveredPlatform()
    {
        var harness = new Harness();
        harness.SetRows(
        [
            .. Enumerable.Range(1, 10).Select(i =>
                new HarvestedCandidateRow("EUW1", $"euw-{i}", 22, 100 + i, 50, Now, IsKnownCandidate: false)),
            .. Enumerable.Range(1, 10).Select(i =>
                new HarvestedCandidateRow("KR", $"kr-{i}", 22, 10 + i, 5, Now, IsKnownCandidate: false))
        ]);

        // EUW1 at target, KR with none: KR's weight is 2 against EUW1's 1.
        var coverage = new ChampionCoverageSnapshot(
            new Dictionary<(string, int), int> { [("EUW1", 22)] = 20 },
            targetMainsPerChampion: 20);

        await harness.RunAsync(maxCandidatesPerRun: 9, platforms: ["EUW1", "KR"], coverage: coverage);

        harness.AddedCandidates.Count(candidate => candidate.PlatformId == "KR").Should().Be(6);
        harness.AddedCandidates.Count(candidate => candidate.PlatformId == "EUW1").Should().Be(3);
    }

    [Fact]
    public async Task HarvestAsync_SpillsAPlatformsUnusedSlice_SoTheRunStillFillsItsBudget()
    {
        // A floor, not a partition: KR is allocated half the budget but only has one row.
        var harness = new Harness();
        harness.SetRows(
        [
            .. Enumerable.Range(1, 10).Select(i =>
                new HarvestedCandidateRow("EUW1", $"euw-{i}", 22, 100 + i, 50, Now, IsKnownCandidate: false)),
            new HarvestedCandidateRow("KR", "kr-1", 22, 11, 5, Now, IsKnownCandidate: false)
        ]);

        var result = await harness.RunAsync(maxCandidatesPerRun: 6, platforms: ["EUW1", "KR"]);

        result.CandidatesInserted.Should().Be(6);
        harness.AddedCandidates.Count(candidate => candidate.PlatformId == "KR").Should().Be(1);
        harness.AddedCandidates.Count(candidate => candidate.PlatformId == "EUW1").Should().Be(5);
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

        /// <summary>
        /// Returns <paramref name="rows"/> as a batch the repository could not truncate:
        /// eligibility is derived from the rows themselves, so coverage comes back complete.
        /// </summary>
        public void SetRows(params HarvestedCandidateRow[] rows)
            => SetBatch(new HarvestCandidateBatch(
                rows,
                rows.GroupBy(row => row.PlatformId, StringComparer.Ordinal)
                    .Select(group => new HarvestPlatformEligibility(
                        group.Key,
                        group.Count(row => !row.IsKnownCandidate),
                        group.Count(row => row.IsKnownCandidate)))
                    .ToList()));

        /// <summary>Explicit batch, for the truncated-pool cases where eligibility exceeds the rows.</summary>
        public void SetBatch(HarvestCandidateBatch batch)
            => Participants.GetHarvestCandidatesAsync(
                    Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(batch));

        public Task<HarvestResult> RunAsync(
            int lookbackDays = 0,
            int maxCandidatesPerRun = 5000,
            double newCandidateShare = 0.5,
            IReadOnlyCollection<string>? platforms = null,
            ChampionCoverageSnapshot? coverage = null)
            // A neutral snapshot splits the budget evenly across the platforms present in the
            // batch (#1150), so a single-platform case behaves exactly as it did before the
            // split existed — which is what keeps the class-share cases below meaningful.
            => new ParticipantHarvestService().HarvestAsync(
                _session,
                new HarvestOptions
                {
                    Platforms = platforms is null ? ["KR"] : [..platforms],
                    MinObservedGames = 5,
                    LookbackDays = lookbackDays,
                    MaxCandidatesPerRun = maxCandidatesPerRun,
                    NewCandidateShare = newCandidateShare
                },
                coverage ?? ChampionCoverageSnapshot.Empty,
                Now,
                CancellationToken.None);
    }
}
