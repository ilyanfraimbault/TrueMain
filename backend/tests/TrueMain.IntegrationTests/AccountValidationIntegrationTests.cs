using Data.Entities;
using AwesomeAssertions;
using Ingestor.Processes.Components.MatchIngestion;
using Microsoft.Extensions.Logging.Abstractions;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class AccountValidationIntegrationTests
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
            TimeProvider.System,
            NullLogger<AccountValidationService>.Instance);

        var validated = await service.ValidateAsync(accountKey, CancellationToken.None);
        validated.Should().BeTrue("a Processing candidate was promoted");

        await using (var verifyDb = _fixture.CreateDbContext())
        {
            var account = verifyDb.RiotAccounts.Single(a => a.PlatformId == accountKey.PlatformId && a.Puuid == accountKey.Puuid);
            var candidate = verifyDb.MainCandidates.Single(c => c.PlatformId == accountKey.PlatformId && c.Puuid == accountKey.Puuid);

            account.MatchIngestStatus.Should().Be(MatchIngestStatus.Idle);
            account.MatchIngestClaimedAtUtc.Should().BeNull();
            account.LastMatchIngestAtUtc.Should().NotBeNull();
            candidate.Status.Should().Be(MainCandidateStatus.Validated);
            candidate.ValidatedAtUtc.Should().NotBeNull(
                "the promotion stamps the column the queue-latency snapshot reads (#1024); "
                + "it used to set the status alone, leaving every row 'never validated'");
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

    [Fact]
    public async Task ValidateAsync_ShouldReportNothingValidated_WhenTheAccountHasNoProcessingCandidate()
    {
        // The funnel counts validated *accounts* off this return value (#1024), so an
        // account whose candidates were already promoted — or reverted out from under
        // the claim — must not inflate the count on the way through.
        await _fixture.ResetDatabaseAsync();
        var accountKey = new Data.Repositories.AccountKey("KR", "puuid-nothing-to-promote");

        await SeedProcessingStateAsync(accountKey);
        await using (var db = _fixture.CreateDbContext())
        {
            var candidate = db.MainCandidates.Single(c => c.Puuid == accountKey.Puuid);
            candidate.Status = MainCandidateStatus.Queued;
            await db.SaveChangesAsync();
        }

        var service = new AccountValidationService(
            _fixture.CreateSessionFactory(),
            TimeProvider.System,
            NullLogger<AccountValidationService>.Instance);

        var validated = await service.ValidateAsync(accountKey, CancellationToken.None);

        validated.Should().BeFalse();
        await using (var verifyDb = _fixture.CreateDbContext())
        {
            var candidate = verifyDb.MainCandidates.Single(c => c.Puuid == accountKey.Puuid);
            candidate.Status.Should().Be(MainCandidateStatus.Queued, "only Processing rows are promoted");
            candidate.ValidatedAtUtc.Should().BeNull();
        }
    }

    [Fact]
    public async Task ReleaseUningestableAsync_ShouldStampTheIngestTimestamp_UnlikeARevert()
    {
        // #1223: a claim released because the account cannot be ingested at all must not
        // behave like a revert. Claims are ordered never-ingested-first then
        // oldest-ingested-first, so leaving LastMatchIngestAtUtc null — which is exactly
        // what a revert does, on purpose, to retry a transient failure at once — hands
        // this permanently unusable row the head of every subsequent batch.
        await _fixture.ResetDatabaseAsync();
        var accountKey = new Data.Repositories.AccountKey("XX9", "puuid-uningestable");

        await SeedProcessingStateAsync(accountKey);

        var service = new AccountValidationService(
            _fixture.CreateSessionFactory(),
            TimeProvider.System,
            NullLogger<AccountValidationService>.Instance);

        await service.ReleaseUningestableAsync(accountKey, CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        var account = verifyDb.RiotAccounts.Single(a => a.PlatformId == accountKey.PlatformId && a.Puuid == accountKey.Puuid);
        var candidate = verifyDb.MainCandidates.Single(c => c.PlatformId == accountKey.PlatformId && c.Puuid == accountKey.Puuid);

        account.LastMatchIngestAtUtc.Should().NotBeNull("the row has to move to the back of the claim ordering");
        account.MatchIngestStatus.Should().Be(MatchIngestStatus.Idle);
        account.MatchIngestClaimedAtUtc.Should().BeNull();

        // The candidate itself is not settled by this — the account was never addressed.
        candidate.Status.Should().Be(MainCandidateStatus.Queued);
        candidate.ValidatedAtUtc.Should().BeNull();
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
