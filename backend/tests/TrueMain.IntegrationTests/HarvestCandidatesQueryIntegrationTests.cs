using AwesomeAssertions;
using Data.Entities;

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
        var rows = await session.MatchParticipants.GetHarvestCandidatesAsync(
            ["KR"], RankedSolo, minObservedGames: 5, maxRows: 100, CancellationToken.None);

        // P1 (6 orphan ranked-solo games on champ 22) is the only one above the gate.
        // Excluded: P2 (3 games < 5), P3 (tracked — RiotAccountId set), P4 (ARAM queue).
        rows.Should().ContainSingle();
        var harvested = rows.Single();
        harvested.PlatformId.Should().Be("KR");
        harvested.Puuid.Should().Be("P1");
        harvested.ChampionId.Should().Be(22);
        harvested.ObservedGames.Should().Be(6);
        harvested.ObservedWins.Should().Be(4);
        harvested.LastSeenUtc.Should().Be(now);
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
        var rows = await session.MatchParticipants.GetHarvestCandidatesAsync(
            ["KR"], RankedSolo, minObservedGames: 5, maxRows: 100, CancellationToken.None);

        rows.Should().HaveCount(2);
        rows.Select(r => r.ChampionId).Should().BeEquivalentTo([22, 64]);
        rows.Should().OnlyContain(r => r.Puuid == "PX" && r.ObservedGames == 5);
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
