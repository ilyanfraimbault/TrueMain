using Data.Entities;
using FluentAssertions;
using Ingestor.Processes.Components.MatchIngestion;
using Microsoft.Extensions.Logging.Abstractions;

namespace TrueMain.IntegrationTests;

public sealed class AccountValidationIntegrationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public AccountValidationIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AccountValidationService_ShouldHandleValidateAndRevertTransitions()
    {
        await _fixture.ResetDatabaseAsync();
        var accountKey = new Data.Repositories.AccountKey("KR", "puuid-1");

        await SeedProcessingStateAsync(accountKey);

        var service = new AccountValidationService(
            _fixture.CreateSessionFactory(),
            NullLogger<AccountValidationService>.Instance);

        await service.ValidateAsync(accountKey, CancellationToken.None);

        await using (var verifyDb = _fixture.CreateDbContext())
        {
            var account = verifyDb.RiotAccounts.Single(a => a.PlatformId == accountKey.PlatformId && a.Puuid == accountKey.Puuid);
            var candidate = verifyDb.MainCandidates.Single(c => c.PlatformId == accountKey.PlatformId && c.Puuid == accountKey.Puuid);

            account.MatchIngestStatus.Should().Be(MatchIngestStatus.Idle);
            account.MatchIngestClaimedAtUtc.Should().BeNull();
            account.LastMatchIngestAtUtc.Should().NotBeNull();
            candidate.Status.Should().Be(MainCandidateStatus.Validated);
        }

        await SetProcessingStateAsync(accountKey);
        await service.RevertAsync(accountKey, CancellationToken.None);

        await using (var verifyDb = _fixture.CreateDbContext())
        {
            var account = verifyDb.RiotAccounts.Single(a => a.PlatformId == accountKey.PlatformId && a.Puuid == accountKey.Puuid);
            var candidate = verifyDb.MainCandidates.Single(c => c.PlatformId == accountKey.PlatformId && c.Puuid == accountKey.Puuid);

            account.MatchIngestStatus.Should().Be(MatchIngestStatus.Idle);
            account.MatchIngestClaimedAtUtc.Should().BeNull();
            candidate.Status.Should().Be(MainCandidateStatus.Queued);
        }
    }

    private async Task SeedProcessingStateAsync(Data.Repositories.AccountKey accountKey)
    {
        await using var db = _fixture.CreateDbContext();
        var now = DateTime.UtcNow;

        db.RiotAccounts.Add(new RiotAccount
        {
            Puuid = accountKey.Puuid,
            PlatformId = accountKey.PlatformId,
            GameName = "player",
            TagLine = "KR1",
            SummonerId = "sum",
            ProfileIconId = 1,
            SummonerLevel = 10,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            MatchIngestStatus = MatchIngestStatus.Processing,
            MatchIngestClaimedAtUtc = now
        });

        db.MainCandidates.Add(new MainCandidate
        {
            PlatformId = accountKey.PlatformId,
            Puuid = accountKey.Puuid,
            ChampionId = 10,
            ChampionRankInMasteryTop = 1,
            ChampionPoints = 1000,
            LastPlayTimeUtc = now,
            DiscoveredAtUtc = now,
            Status = MainCandidateStatus.Processing
        });

        await db.SaveChangesAsync();
    }

    private async Task SetProcessingStateAsync(Data.Repositories.AccountKey accountKey)
    {
        await using var db = _fixture.CreateDbContext();
        var account = db.RiotAccounts.Single(a => a.PlatformId == accountKey.PlatformId && a.Puuid == accountKey.Puuid);
        var candidate = db.MainCandidates.Single(c => c.PlatformId == accountKey.PlatformId && c.Puuid == accountKey.Puuid);

        account.MatchIngestStatus = MatchIngestStatus.Processing;
        account.MatchIngestClaimedAtUtc = DateTime.UtcNow;
        candidate.Status = MainCandidateStatus.Processing;

        await db.SaveChangesAsync();
    }
}
