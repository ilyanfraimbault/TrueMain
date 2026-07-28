using Data.Entities;
using Data.Repositories;
using AwesomeAssertions;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Dedicated coverage for <see cref="MainCandidateRepository.SetStatusForAccountsAsync"/>
/// (#858): the batch transition groups by platform and de-duplicates by puuid before
/// mutating, so an account with several candidate rows (one per champion) must still
/// only be reported once, and a batch spanning multiple platforms must update and
/// report every platform's accounts. This was previously only exercised indirectly via
/// <see cref="MainAnalysisProcessIntegrationTests"/>, where every test account has just
/// one candidate champion.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class MainCandidateRepositoryIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public MainCandidateRepositoryIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SetStatusForAccountsAsync_ShouldCountAccountOnce_WhenItHasMultipleCandidateChampions()
    {
        await _fixture.ResetDatabaseAsync();

        const string platformId = "KR";
        const string puuid = "puuid-multi-champ";

        await SeedCandidateAsync(platformId, puuid, championId: 22, MainCandidateStatus.Validated);
        await SeedCandidateAsync(platformId, puuid, championId: 51, MainCandidateStatus.Validated);
        await SeedCandidateAsync(platformId, puuid, championId: 103, MainCandidateStatus.Validated);

        await using var db = _fixture.CreateDbContext();
        var repository = new MainCandidateRepository(db);

        var affected = await repository.SetStatusForAccountsAsync(
            [new AccountKey(platformId, puuid)],
            MainCandidateStatus.Validated,
            MainCandidateStatus.Processing,
            CancellationToken.None);

        affected.Should().ContainSingle(a => a.PlatformId == platformId && a.Puuid == puuid,
            "an account with several candidate rows (one per champion) must be reported exactly once");

        await using var verifyDb = _fixture.CreateDbContext();
        var statuses = verifyDb.MainCandidates
            .Where(c => c.PlatformId == platformId && c.Puuid == puuid)
            .Select(c => c.Status)
            .ToList();

        statuses.Should().HaveCount(3);
        statuses.Should().AllSatisfy(status => status.Should().Be(MainCandidateStatus.Processing),
            "every candidate row for the account must transition, not just one");
    }

    [Fact]
    public async Task SetStatusForAccountsAsync_ShouldGroupAndUpdateAcrossMultiplePlatforms()
    {
        await _fixture.ResetDatabaseAsync();

        var accounts = new[]
        {
            (PlatformId: "KR", Puuid: "puuid-kr-1"),
            (PlatformId: "KR", Puuid: "puuid-kr-2"),
            (PlatformId: "EUW1", Puuid: "puuid-euw-1"),
            (PlatformId: "NA1", Puuid: "puuid-na-1")
        };

        foreach (var account in accounts)
        {
            await SeedCandidateAsync(account.PlatformId, account.Puuid, championId: 22, MainCandidateStatus.Validated);
        }

        // An unrelated candidate that must not be touched: same puuid as one of the
        // batch accounts, but on a platform outside the requested batch.
        await SeedCandidateAsync("BR1", "puuid-kr-1", championId: 22, MainCandidateStatus.Validated);

        await using var db = _fixture.CreateDbContext();
        var repository = new MainCandidateRepository(db);

        var affected = await repository.SetStatusForAccountsAsync(
            accounts.Select(a => new AccountKey(a.PlatformId, a.Puuid)).ToList(),
            MainCandidateStatus.Validated,
            MainCandidateStatus.Processing,
            CancellationToken.None);

        affected.Should().HaveCount(4);
        affected.Should().BeEquivalentTo(
            accounts.Select(a => new AccountKey(a.PlatformId, a.Puuid)),
            "every account across every requested platform must be reported as affected");

        await using var verifyDb = _fixture.CreateDbContext();
        foreach (var account in accounts)
        {
            var status = verifyDb.MainCandidates
                .Single(c => c.PlatformId == account.PlatformId && c.Puuid == account.Puuid)
                .Status;
            status.Should().Be(MainCandidateStatus.Processing);
        }

        var untouched = verifyDb.MainCandidates
            .Single(c => c.PlatformId == "BR1" && c.Puuid == "puuid-kr-1")
            .Status;
        untouched.Should().Be(MainCandidateStatus.Validated,
            "a candidate outside the requested (platform, puuid) batch must not be touched, even if its puuid matches another platform's entry");
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
            LastPlayTimeUtc = now.AddDays(-1),
            DiscoveredAtUtc = now.AddDays(-2),
            Score = 90,
            Status = status,
            ScoredAtUtc = now.AddDays(-2),
            ValidatedAtUtc = status == MainCandidateStatus.Validated ? now.AddDays(-1) : null
        });

        await db.SaveChangesAsync();
    }
}
