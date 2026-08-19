using AwesomeAssertions;
using Data.Entities;
using Data.Repositories;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Per-platform allocation in the match-ingest claim (#1150).
///
/// <para>
/// The claim used to be one cross-platform ordering by <c>LastMatchIngestAtUtc</c>, nulls
/// first. That is the right priority <em>within</em> a region — a never-ingested account
/// before an already-ingested one — but across regions it made the batch a mirror of the
/// account pool, and the pool was ~82% one region because the previous batch had just fed it.
/// These tests seed exactly that shape: one region with far more claimable accounts than the
/// others, all never-ingested, so a claim without quotas takes essentially only that region.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class RiotAccountClaimPlatformQuotaIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public RiotAccountClaimPlatformQuotaIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ClaimAsync_RespectsPerPlatformQuotas_WhenOneRegionDominatesThePool()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        // The prod shape, scaled down: EUW1 has 30 claimable accounts, KR and NA1 have 10.
        await SeedQueuedAccountsAsync("EUW1", 30, now);
        await SeedQueuedAccountsAsync("KR", 10, now);
        await SeedQueuedAccountsAsync("NA1", 10, now);

        var claimed = await ClaimAsync(
            new Dictionary<string, int> { ["EUW1"] = 3, ["KR"] = 6, ["NA1"] = 3 },
            batchSize: 12,
            now);

        claimed.Should().HaveCount(12);
        ByPlatform(claimed).Should().BeEquivalentTo(new Dictionary<string, int>
        {
            ["EUW1"] = 3,
            ["KR"] = 6,
            ["NA1"] = 3
        });
    }

    [Fact]
    public async Task ClaimAsync_SpillsAnUnfillableQuota_RatherThanShrinkingTheBatch()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        // KR is allocated 6 but only has 2 claimable accounts. A quota is a floor, not a
        // partition: the run must still spend its whole batch.
        await SeedQueuedAccountsAsync("EUW1", 30, now);
        await SeedQueuedAccountsAsync("KR", 2, now);
        await SeedQueuedAccountsAsync("NA1", 30, now);

        var claimed = await ClaimAsync(
            new Dictionary<string, int> { ["EUW1"] = 3, ["KR"] = 6, ["NA1"] = 3 },
            batchSize: 12,
            now);

        claimed.Should().HaveCount(12);

        var byPlatform = ByPlatform(claimed);
        byPlatform["KR"].Should().Be(2);
        // The 4 released slots are spread round-robin, not handed whole to whichever platform
        // sorts first — a "next platform takes the remainder" spill is just the cross-platform
        // ordering again, and would restore the imbalance the quotas exist to correct.
        byPlatform["EUW1"].Should().Be(5);
        byPlatform["NA1"].Should().Be(5);
    }

    [Fact]
    public async Task ClaimAsync_KeepsNullsFirstWithinAPlatform()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        // Two KR accounts: one already ingested an hour ago, one never ingested. Scoping the
        // claim per platform must not lose the priority that made sense inside one.
        await SeedQueuedAccountsAsync("KR", 1, now, puuidPrefix: "old", lastIngestAtUtc: now.AddHours(-1));
        await SeedQueuedAccountsAsync("KR", 1, now, puuidPrefix: "new");

        var claimed = await ClaimAsync(new Dictionary<string, int> { ["KR"] = 1 }, batchSize: 1, now);

        claimed.Should().ContainSingle().Which.Puuid.Should().StartWith("new");
    }

    private static Dictionary<string, int> ByPlatform(IEnumerable<AccountKey> claimed)
        => claimed
            .GroupBy(key => key.PlatformId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    private async Task<List<AccountKey>> ClaimAsync(
        Dictionary<string, int> quotas,
        int batchSize,
        DateTime nowUtc)
    {
        await using var db = _fixture.CreateDbContext();
        var repo = new RiotAccountRepository(db);

        // establishedMainShare 0 keeps these tests on the queued-candidate arm only: the
        // platform split is what is under test, not the class split (#900 covers that).
        return await repo.ClaimAccountsForMatchIngestAtomicallyAsync(
            quotas,
            batchSize,
            establishedMainShare: 0,
            nowUtc,
            TimeSpan.FromMinutes(30),
            CancellationToken.None);
    }

    private async Task SeedQueuedAccountsAsync(
        string platformId,
        int count,
        DateTime now,
        string puuidPrefix = "puuid",
        DateTime? lastIngestAtUtc = null)
    {
        await using var db = _fixture.CreateDbContext();

        for (var i = 1; i <= count; i++)
        {
            var puuid = $"{puuidPrefix}-{platformId}-{i}";
            db.RiotAccounts.Add(new RiotAccount
            {
                Puuid = puuid,
                PlatformId = platformId,
                GameName = $"player-{platformId}-{i}",
                TagLine = "TAG",
                SummonerId = $"sum-{platformId}-{i}",
                ProfileIconId = 1,
                SummonerLevel = 100,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                LastMatchIngestAtUtc = lastIngestAtUtc,
                MatchIngestStatus = MatchIngestStatus.Idle
            });

            db.MainCandidates.Add(new MainCandidate
            {
                PlatformId = platformId,
                Puuid = puuid,
                ChampionId = 1,
                ChampionRankInMasteryTop = 1,
                ChampionPoints = 1000,
                LastPlayTimeUtc = now,
                DiscoveredAtUtc = now,
                Status = MainCandidateStatus.Queued
            });
        }

        await db.SaveChangesAsync();
    }
}
