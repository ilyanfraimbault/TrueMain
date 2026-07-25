using AwesomeAssertions;
using Data;
using Data.Entities;
using Data.Repositories;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class HarvestCandidatesQueryIntegrationTests
{
    private const int RankedSolo = 420;
    private const int Aram = 450;

    private readonly PostgresFixture _fixture;

    public HarvestCandidatesQueryIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetHarvestCandidatesAsync_AggregatesOrphanRows_ApplyingQueueAndThresholdFilters()
    {
        await _fixture.ResetDatabaseAsync();
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);
        await SeedAsync(now);

        await using var session = await _fixture.CreateSessionFactory().CreateAsync(CancellationToken.None);
        var batch = await session.MatchParticipants.GetHarvestCandidatesAsync(
            ["KR"], RankedSolo, minObservedGames: 5, maxRowsPerBucket: 100, DateTime.UnixEpoch, CancellationToken.None);

        // P1 (6 orphan ranked-solo games on champ 22) is the only one above the gate.
        // Excluded: P2 (3 games < 5), P3 (tracked — RiotAccountId set), P4 (ARAM queue).
        batch.Rows.Should().ContainSingle();
        var harvested = batch.Rows.Single();
        harvested.PlatformId.Should().Be("KR");
        harvested.Puuid.Should().Be("P1");
        harvested.ChampionId.Should().Be(22);
        harvested.ObservedGames.Should().Be(6);
        harvested.ObservedWins.Should().Be(4);
        harvested.LastSeenUtc.Should().Be(now);
        // No candidate row exists for it yet, so it is new discovery (#495).
        harvested.IsKnownCandidate.Should().BeFalse();
        batch.Eligibility.Should().ContainSingle();
        batch.Eligibility.Single().Should().BeEquivalentTo(
            new { PlatformId = "KR", EligibleNew = 1, EligibleKnown = 0 });
    }

    [Fact]
    public async Task GetHarvestCandidatesAsync_GroupsByChampion_ForSamePuuid()
    {
        await _fixture.ResetDatabaseAsync();
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

        await using (var db = _fixture.CreateDbContext())
        {
            // Same puuid, two champions, each above the gate -> two distinct rows.
            for (var i = 0; i < 5; i++)
            {
                MatchParticipantSeed.AddMatchWithParticipant(db, $"GRP_A_{i}", "KR", RankedSolo, now.AddDays(-i), "PX", 22, win: true);
                MatchParticipantSeed.AddMatchWithParticipant(db, $"GRP_B_{i}", "KR", RankedSolo, now.AddDays(-i), "PX", 64, win: false);
            }

            await db.SaveChangesAsync();
        }

        await using var session = await _fixture.CreateSessionFactory().CreateAsync(CancellationToken.None);
        var batch = await session.MatchParticipants.GetHarvestCandidatesAsync(
            ["KR"], RankedSolo, minObservedGames: 5, maxRowsPerBucket: 100, DateTime.UnixEpoch, CancellationToken.None);

        batch.Rows.Should().HaveCount(2);
        batch.Rows.Select(r => r.ChampionId).Should().BeEquivalentTo([22, 64]);
        batch.Rows.Should().OnlyContain(r => r.Puuid == "PX" && r.ObservedGames == 5);
    }

    [Fact]
    public async Task GetHarvestCandidatesAsync_ExcludesMatchesBeforeSinceUtc()
    {
        await _fixture.ResetDatabaseAsync();
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

        await using (var db = _fixture.CreateDbContext())
        {
            // PR: 6 recent orphan games (within the window). POLD: 6 games all older than the
            // cutoff -> excluded entirely by the date filter.
            for (var i = 0; i < 6; i++)
            {
                MatchParticipantSeed.AddMatchWithParticipant(db, $"REC_{i}", "KR", RankedSolo, now.AddDays(-i), "PR", 22, win: true);
                MatchParticipantSeed.AddMatchWithParticipant(db, $"OLD_{i}", "KR", RankedSolo, now.AddDays(-30 - i), "POLD", 22, win: true);
            }

            await db.SaveChangesAsync();
        }

        await using var session = await _fixture.CreateSessionFactory().CreateAsync(CancellationToken.None);
        var batch = await session.MatchParticipants.GetHarvestCandidatesAsync(
            ["KR"], RankedSolo, minObservedGames: 5, maxRowsPerBucket: 100, sinceUtc: now.AddDays(-10), CancellationToken.None);

        batch.Rows.Should().ContainSingle();
        batch.Rows.Single().Puuid.Should().Be("PR");
    }

    [Fact]
    public async Task GetHarvestCandidatesAsync_RespectsMaxRowsPerBucket_OrderedByObservedGamesDesc()
    {
        await _fixture.ResetDatabaseAsync();
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

        await using (var db = _fixture.CreateDbContext())
        {
            // Three eligible (puuid, champion): 8, 7 and 6 observed games respectively.
            SeedGames(db, "PA", gameCount: 8, now);
            SeedGames(db, "PB", gameCount: 7, now);
            SeedGames(db, "PC", gameCount: 6, now);
            await db.SaveChangesAsync();
        }

        await using var session = await _fixture.CreateSessionFactory().CreateAsync(CancellationToken.None);
        var batch = await session.MatchParticipants.GetHarvestCandidatesAsync(
            ["KR"], RankedSolo, minObservedGames: 5, maxRowsPerBucket: 2, DateTime.UnixEpoch, CancellationToken.None);

        // Truncated to the top 2 by observed games (PC with 6 drops off)...
        batch.Rows.Should().HaveCount(2);
        batch.Rows.Select(r => r.ObservedGames).Should().Equal(8, 7);
        batch.Rows.Select(r => r.Puuid).Should().Equal("PA", "PB");
        // ...but the eligible total is counted before the cap, so the caller can report
        // what it left behind instead of truncating silently (#495).
        batch.Eligibility.Single().EligibleNew.Should().Be(3);
    }

    [Fact]
    public async Task GetHarvestCandidatesAsync_MergesAcrossPlatforms_CappingEachPlatformSlice()
    {
        await _fixture.ResetDatabaseAsync();
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

        await using (var db = _fixture.CreateDbContext())
        {
            // Eligible (puuid, champion) spread across the three harvested platforms with
            // distinct observed-game counts. The per-platform chunking (#632) must still
            // return each platform's own top-N, since the caller's budget is applied over
            // the union: any row it can select is necessarily inside its platform's slice.
            SeedGamesOn(db, "KR", "PA", gameCount: 8, now);   // KR, 8 games
            SeedGamesOn(db, "KR", "PB", gameCount: 7, now);   // KR, 7 games
            SeedGamesOn(db, "EUW1", "PE", gameCount: 6, now); // EUW1, 6 games
            SeedGamesOn(db, "NA1", "PN", gameCount: 5, now);  // NA1, 5 games
            await db.SaveChangesAsync();
        }

        await using var session = await _fixture.CreateSessionFactory().CreateAsync(CancellationToken.None);
        var batch = await session.MatchParticipants.GetHarvestCandidatesAsync(
            ["KR", "EUW1", "NA1"], RankedSolo, minObservedGames: 5, maxRowsPerBucket: 1, DateTime.UnixEpoch, CancellationToken.None);

        // One row per platform (the cap is per platform and per class), each being that
        // platform's most-observed pair: KR keeps PA(8) and drops PB(7).
        batch.Rows.Should().HaveCount(3);
        batch.Rows.Select(r => r.Puuid).Should().BeEquivalentTo(["PA", "PE", "PN"]);
        batch.Rows.Should().OnlyContain(r => !r.IsKnownCandidate);

        // Eligibility stays exact per platform even where the slice was cut (KR: 2 of 2
        // eligible, only 1 returned), so an imbalanced run is visible to the caller.
        batch.Eligibility.Should().BeEquivalentTo(
        [
            new HarvestPlatformEligibility("KR", 2, 0),
            new HarvestPlatformEligibility("EUW1", 1, 0),
            new HarvestPlatformEligibility("NA1", 1, 0)
        ]);
    }

    [Fact]
    public async Task GetHarvestCandidatesAsync_ReturnsNewPairs_WhenKnownPairsAlreadyFillTheCap()
    {
        await _fixture.ResetDatabaseAsync();
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

        await using (var db = _fixture.CreateDbContext())
        {
            // The starvation setup (#495): three heavily-observed pairs that are ALREADY
            // harvest candidates, and one pair that just crossed the gate with the fewest
            // games. Under a single top-N ordered by observed games and cut at the cap, the
            // known three would take every slot and the newcomer would never be harvested.
            SeedGames(db, "KNOWN_A", gameCount: 30, now);
            SeedGames(db, "KNOWN_B", gameCount: 20, now);
            SeedGames(db, "KNOWN_C", gameCount: 10, now);
            SeedGames(db, "NEWCOMER", gameCount: 5, now);
            AddCandidate(db, "KNOWN_A", MainCandidateSource.Harvest, MainCandidateStatus.Queued, now);
            AddCandidate(db, "KNOWN_B", MainCandidateSource.Harvest, MainCandidateStatus.Scored, now);
            AddCandidate(db, "KNOWN_C", MainCandidateSource.Harvest, MainCandidateStatus.Validated, now);
            await db.SaveChangesAsync();
        }

        await using var session = await _fixture.CreateSessionFactory().CreateAsync(CancellationToken.None);
        var batch = await session.MatchParticipants.GetHarvestCandidatesAsync(
            ["KR"], RankedSolo, minObservedGames: 5, maxRowsPerBucket: 2, DateTime.UnixEpoch, CancellationToken.None);

        // The cap bites on the known class only: it keeps its own top 2, while the newcomer
        // still comes back — the two classes are ranked and capped independently.
        batch.Rows.Where(r => r.IsKnownCandidate).Select(r => r.Puuid).Should().Equal("KNOWN_A", "KNOWN_B");
        batch.Rows.Where(r => !r.IsKnownCandidate).Select(r => r.Puuid).Should().Equal("NEWCOMER");

        // And the run can say exactly how much of the known class it skipped.
        batch.Eligibility.Single().Should().BeEquivalentTo(
            new { PlatformId = "KR", EligibleNew = 1, EligibleKnown = 3 });
    }

    [Fact]
    public async Task GetHarvestCandidatesAsync_ExcludesPairsTheHarvestCannotAdvance()
    {
        await _fixture.ResetDatabaseAsync();
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

        await using (var db = _fixture.CreateDbContext())
        {
            SeedGames(db, "REJECTED", gameCount: 30, now);
            SeedGames(db, "LADDER", gameCount: 20, now);
            SeedGames(db, "SEEDED", gameCount: 15, now);
            SeedGames(db, "REFRESHABLE", gameCount: 10, now);
            // A rejection is a verdict from real history + MainAnalysis, and ladder /
            // manual-seed candidates must keep their own stats — the harvest can do nothing
            // with either, so they must not consume the run's budget (#495).
            AddCandidate(db, "REJECTED", MainCandidateSource.Harvest, MainCandidateStatus.Rejected, now);
            AddCandidate(db, "LADDER", MainCandidateSource.Ladder, MainCandidateStatus.Scored, now);
            AddCandidate(db, "SEEDED", MainCandidateSource.ManualSeed, MainCandidateStatus.New, now);
            AddCandidate(db, "REFRESHABLE", MainCandidateSource.Harvest, MainCandidateStatus.Scored, now);
            await db.SaveChangesAsync();
        }

        await using var session = await _fixture.CreateSessionFactory().CreateAsync(CancellationToken.None);
        var batch = await session.MatchParticipants.GetHarvestCandidatesAsync(
            ["KR"], RankedSolo, minObservedGames: 5, maxRowsPerBucket: 100, DateTime.UnixEpoch, CancellationToken.None);

        batch.Rows.Should().ContainSingle();
        batch.Rows.Single().Puuid.Should().Be("REFRESHABLE");
        batch.Rows.Single().IsKnownCandidate.Should().BeTrue();
        // The excluded pairs are not eligible at all — they are not silently dropped from a
        // class the caller believes it covered.
        batch.Eligibility.Single().Should().BeEquivalentTo(
            new { PlatformId = "KR", EligibleNew = 0, EligibleKnown = 1 });
    }

    private static void AddCandidate(
        TrueMainDbContext db,
        string puuid,
        MainCandidateSource source,
        MainCandidateStatus status,
        DateTime now)
    {
        db.MainCandidates.Add(new MainCandidate
        {
            PlatformId = "KR",
            Puuid = puuid,
            ChampionId = 22,
            Source = source,
            Status = status,
            LastPlayTimeUtc = now.AddDays(-1),
            DiscoveredAtUtc = now.AddDays(-1)
        });
    }

    private static void SeedGamesOn(TrueMainDbContext db, string platformId, string puuid, int gameCount, DateTime now)
    {
        for (var i = 0; i < gameCount; i++)
        {
            MatchParticipantSeed.AddMatchWithParticipant(
                db, $"{platformId}_{puuid}_{i}", platformId, RankedSolo, now.AddDays(-i), puuid, 22, win: true);
        }
    }

    private static void SeedGames(TrueMainDbContext db, string puuid, int gameCount, DateTime now)
    {
        for (var i = 0; i < gameCount; i++)
        {
            MatchParticipantSeed.AddMatchWithParticipant(
                db, $"{puuid}_{i}", "KR", RankedSolo, now.AddDays(-i), puuid, 22, win: true);
        }
    }

    private async Task SeedAsync(DateTime now)
    {
        await using var db = _fixture.CreateDbContext();

        // A tracked account so P3's participant rows can carry a non-null RiotAccountId FK.
        var trackedAccountId = Guid.NewGuid();
        db.RiotAccounts.Add(new RiotAccount
        {
            Id = trackedAccountId,
            Puuid = "P3",
            PlatformId = "KR",
            GameName = "Tracked",
            TagLine = "KR1",
            ProfileIconId = 1,
            SummonerLevel = 200,
            UpdatedAtUtc = now
        });

        // P1: 6 orphan ranked-solo games on champ 22, 4 wins, most recent at `now`.
        for (var i = 0; i < 6; i++)
        {
            MatchParticipantSeed.AddMatchWithParticipant(db, $"P1_{i}", "KR", RankedSolo, now.AddDays(-i), "P1", 22, win: i < 4);
        }

        // P2: only 3 orphan games -> below the gate.
        for (var i = 0; i < 3; i++)
        {
            MatchParticipantSeed.AddMatchWithParticipant(db, $"P2_{i}", "KR", RankedSolo, now.AddDays(-i), "P2", 22, win: true);
        }

        // P3: 6 ranked-solo games but TRACKED (RiotAccountId set) -> excluded.
        for (var i = 0; i < 6; i++)
        {
            MatchParticipantSeed.AddMatchWithParticipant(db, $"P3_{i}", "KR", RankedSolo, now.AddDays(-i), "P3", 22, win: true, riotAccountId: trackedAccountId);
        }

        // P4: 6 orphan games but in ARAM -> excluded by the queue filter.
        for (var i = 0; i < 6; i++)
        {
            MatchParticipantSeed.AddMatchWithParticipant(db, $"P4_{i}", "KR", Aram, now.AddDays(-i), "P4", 22, win: true);
        }

        await db.SaveChangesAsync();
    }
}
