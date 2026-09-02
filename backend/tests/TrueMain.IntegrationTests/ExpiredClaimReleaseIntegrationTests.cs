using Data.Entities;
using Data.Repositories;
using AwesomeAssertions;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The lease reaper (#1344). <c>Processing</c> is a lease state, but nothing enforced the
/// lease on the candidate rows: a run that died holding a claim left them there, and the
/// claim query could not recover them either, because it only reaches accounts that hold an
/// active main or a <see cref="MainCandidateStatus.Queued"/> candidate — which an account
/// whose candidates are <em>all</em> stuck at Processing is not. The leak sealed itself, and
/// production accumulated 386 permanently unreachable accounts over three months.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ExpiredClaimReleaseIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public ExpiredClaimReleaseIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ReleaseExpiredClaimsAsync_ShouldReleaseEveryCandidateRowOfAnExpiredClaim()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        await SeedAccountAsync("KR", "puuid-expired", MatchIngestStatus.Processing, now.AddHours(-2));
        await SeedCandidateAsync("KR", "puuid-expired", championId: 22, MainCandidateStatus.Processing);
        await SeedCandidateAsync("KR", "puuid-expired", championId: 51, MainCandidateStatus.Processing);

        await using var db = _fixture.CreateDbContext();
        var released = await new MainCandidateRepository(db)
            .ReleaseExpiredClaimsAsync(now.AddMinutes(-30), CancellationToken.None);

        released.Should().Be(2, "an account carries one candidate row per champion and the claim covered all of them");

        await using var verifyDb = _fixture.CreateDbContext();
        verifyDb.MainCandidates
            .Where(c => c.Puuid == "puuid-expired")
            .Select(c => c.Status)
            .ToList()
            .Should()
            .AllSatisfy(status => status.Should().Be(MainCandidateStatus.Queued));
    }

    [Fact]
    public async Task ReleaseExpiredClaimsAsync_ShouldLeaveALiveClaimAlone()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        await SeedAccountAsync("KR", "puuid-live", MatchIngestStatus.Processing, now.AddMinutes(-5));
        await SeedCandidateAsync("KR", "puuid-live", championId: 22, MainCandidateStatus.Processing);

        await using var db = _fixture.CreateDbContext();
        var released = await new MainCandidateRepository(db)
            .ReleaseExpiredClaimsAsync(now.AddMinutes(-30), CancellationToken.None);

        released.Should().Be(0, "a run still inside its lease owns its candidates — reaping them would double-ingest the account");

        await using var verifyDb = _fixture.CreateDbContext();
        verifyDb.MainCandidates.Single(c => c.Puuid == "puuid-live").Status
            .Should().Be(MainCandidateStatus.Processing);
    }

    [Fact]
    public async Task ReleaseExpiredClaimsAsync_ShouldReleaseCandidatesNoClaimStandsBehind()
    {
        // Two shapes that an "expired claim" predicate would miss, both of which mean the
        // same thing — nothing is working on these rows: an account back at Idle whose
        // candidates were never settled, and a candidate whose account row is gone entirely.
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        await SeedAccountAsync("KR", "puuid-idle", MatchIngestStatus.Idle, claimedAtUtc: null);
        await SeedCandidateAsync("KR", "puuid-idle", championId: 22, MainCandidateStatus.Processing);
        await SeedCandidateAsync("KR", "puuid-no-account", championId: 22, MainCandidateStatus.Processing);

        await using var db = _fixture.CreateDbContext();
        var released = await new MainCandidateRepository(db)
            .ReleaseExpiredClaimsAsync(now.AddMinutes(-30), CancellationToken.None);

        released.Should().Be(2);

        await using var verifyDb = _fixture.CreateDbContext();
        verifyDb.MainCandidates
            .Select(c => c.Status)
            .ToList()
            .Should()
            .AllSatisfy(status => status.Should().Be(MainCandidateStatus.Queued));
    }

    [Fact]
    public async Task ReleaseExpiredMatchIngestClaimsAsync_ShouldIdleExpiredAccountsAndKeepLiveOnes()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        await SeedAccountAsync("KR", "puuid-expired", MatchIngestStatus.Processing, now.AddHours(-2));
        await SeedAccountAsync("KR", "puuid-unstamped", MatchIngestStatus.Processing, claimedAtUtc: null);
        await SeedAccountAsync("KR", "puuid-live", MatchIngestStatus.Processing, now.AddMinutes(-5));

        await using var db = _fixture.CreateDbContext();
        var released = await new RiotAccountRepository(db)
            .ReleaseExpiredMatchIngestClaimsAsync(now.AddMinutes(-30), CancellationToken.None);

        released.Should().Be(2, "a Processing row with no stamp is as unheld as one whose stamp aged out");

        await using var verifyDb = _fixture.CreateDbContext();
        var released1 = verifyDb.RiotAccounts.Single(a => a.Puuid == "puuid-expired");
        released1.MatchIngestStatus.Should().Be(MatchIngestStatus.Idle);
        released1.MatchIngestClaimedAtUtc.Should().BeNull("the stale stamp is what made the next claim's age reading a lie");

        verifyDb.RiotAccounts.Single(a => a.Puuid == "puuid-live").MatchIngestStatus
            .Should().Be(MatchIngestStatus.Processing);
    }

    [Fact]
    public async Task ReleaseExpiredClaimsAsync_ShouldMakeASelfSealedAccountClaimableAgain()
    {
        // The regression that matters. Before the reap the account matches neither claim
        // membership predicate — no active main, no Queued candidate — so it is invisible to
        // the only mechanism that would have settled its rows, however long its lease has
        // been dead. This is the state 386 production accounts were stuck in.
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        await SeedAccountAsync("KR", "puuid-sealed", MatchIngestStatus.Processing, now.AddHours(-2));
        await SeedCandidateAsync("KR", "puuid-sealed", championId: 22, MainCandidateStatus.Processing);

        (await ClaimAsync(now)).Should().BeEmpty("this is the leak: nothing can reach the account while its only candidate sits at Processing");

        await using (var db = _fixture.CreateDbContext())
        {
            await new MainCandidateRepository(db).ReleaseExpiredClaimsAsync(now.AddMinutes(-30), CancellationToken.None);
            await new RiotAccountRepository(db).ReleaseExpiredMatchIngestClaimsAsync(now.AddMinutes(-30), CancellationToken.None);
        }

        var claimed = await ClaimAsync(now);
        claimed.Should().ContainSingle(account => account.Puuid == "puuid-sealed",
            "once its candidate is back at Queued the account is reachable by the claim again");
    }

    private async Task<List<AccountKey>> ClaimAsync(DateTime nowUtc)
    {
        await using var db = _fixture.CreateDbContext();
        return await new RiotAccountRepository(db).ClaimAccountsForMatchIngestAtomicallyAsync(
            new Dictionary<string, int> { ["KR"] = 5 },
            5,
            0.7,
            nowUtc,
            TimeSpan.FromMinutes(30),
            CancellationToken.None);
    }

    private async Task SeedAccountAsync(string platformId, string puuid, MatchIngestStatus status, DateTime? claimedAtUtc)
    {
        await using var db = _fixture.CreateDbContext();
        var now = DateTime.UtcNow;

        db.RiotAccounts.Add(new RiotAccount
        {
            Puuid = puuid,
            PlatformId = platformId,
            GameName = puuid,
            TagLine = "KR1",
            SummonerId = $"sum-{puuid}",
            ProfileIconId = 1,
            SummonerLevel = 100,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            MatchIngestStatus = status,
            MatchIngestClaimedAtUtc = claimedAtUtc
        });

        await db.SaveChangesAsync();
    }

    private async Task SeedCandidateAsync(string platformId, string puuid, int championId, MainCandidateStatus status)
    {
        await using var db = _fixture.CreateDbContext();
        var now = DateTime.UtcNow;

        db.MainCandidates.Add(new MainCandidate
        {
            PlatformId = platformId,
            Puuid = puuid,
            ChampionId = championId,
            ChampionRankInMasteryTop = 1,
            ChampionPoints = 1000,
            LastPlayTimeUtc = now.AddDays(-1),
            DiscoveredAtUtc = now.AddDays(-2),
            Score = 90,
            ScoredAtUtc = now.AddDays(-2),
            Status = status
        });

        await db.SaveChangesAsync();
    }
}
