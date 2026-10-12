using Core.Lol.Map;
using Core.Options;
using Data.Entities;
using Data.Repositories;
using AwesomeAssertions;
using Ingestor.Options;
using Ingestor.Processes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The candidate side of #1535: a candidate whose account was already ingested is no longer
/// promoted, and retention returns its <c>Queued</c> rows to <c>Scored</c> — bounded per
/// platform, never deleted — while a never-ingested account's rows are left alone.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class IngestedAccountCandidatesIntegrationTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;

    public IngestedAccountCandidatesIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Retention_SettlesQueuedRowsOfIngestedAccounts_AndLeavesNeverIngestedOnesQueued()
    {
        var now = DateTime.UtcNow;

        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.AddRange(
                Account("ingested", "KR", now, lastMatchIngestAtUtc: now.AddDays(-3)),
                Account("never", "KR", now, lastMatchIngestAtUtc: null));
            db.MainCandidates.AddRange(
                Candidate("ingested", 1, MainCandidateStatus.Queued, now, platformId: "KR"),
                Candidate("never", 2, MainCandidateStatus.Queued, now, platformId: "KR"),
                // No riot_accounts row yet: never ingested by definition.
                Candidate("no-account", 3, MainCandidateStatus.Queued, now, platformId: "KR"));
            await db.SaveChangesAsync();
        }

        await BuildProcess(new IntakeOptions
            {
                // The depth cap is off so only the settle pass can move a row.
                MaxQueuedPerPlatform = 0,
                QueueDepthDemotionBatchSize = 10,
                MaxDemotionBatchesPerRun = 4
            })
            .RunCoreAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        var byPuuid = await verifyDb.MainCandidates.AsNoTracking()
            .ToDictionaryAsync(c => c.Puuid, c => c.Status);

        // Settled, never deleted (#900): the row is still the record that the pair was seen.
        byPuuid.Should().HaveCount(3);
        byPuuid["ingested"].Should().Be(MainCandidateStatus.Scored);
        byPuuid["never"].Should().Be(MainCandidateStatus.Queued);
        byPuuid["no-account"].Should().Be(MainCandidateStatus.Queued);
    }

    [Fact]
    public async Task Retention_BoundsTheSettlePerPlatform_ByBatchSizeTimesBatchesPerRun()
    {
        var now = DateTime.UtcNow;

        await using (var db = _fixture.CreateDbContext())
        {
            // Five ingested EUW1 accounts against a bound of 1 x 2 per run, and one KR account
            // whose own bound is untouched by EUW1's backlog.
            for (var i = 1; i <= 5; i++)
            {
                db.RiotAccounts.Add(Account($"euw-{i}", "EUW1", now, lastMatchIngestAtUtc: now.AddDays(-i)));
                db.MainCandidates.Add(Candidate($"euw-{i}", i, MainCandidateStatus.Queued, now, platformId: "EUW1"));
            }

            db.RiotAccounts.Add(Account("kr-1", "KR", now, lastMatchIngestAtUtc: now.AddDays(-1)));
            db.MainCandidates.Add(Candidate("kr-1", 10, MainCandidateStatus.Queued, now, platformId: "KR"));
            await db.SaveChangesAsync();
        }

        var process = BuildProcess(new IntakeOptions
        {
            MaxQueuedPerPlatform = 0,
            QueueDepthDemotionBatchSize = 1,
            MaxDemotionBatchesPerRun = 2
        });

        await process.RunCoreAsync(CancellationToken.None);

        await using (var verifyDb = _fixture.CreateDbContext())
        {
            var statuses = await verifyDb.MainCandidates.AsNoTracking()
                .Select(c => new { c.PlatformId, c.Status })
                .ToListAsync();

            statuses.Count(s => s.PlatformId == "EUW1" && s.Status == MainCandidateStatus.Scored).Should().Be(2);
            statuses.Count(s => s.PlatformId == "EUW1" && s.Status == MainCandidateStatus.Queued).Should().Be(3);
            statuses.Single(s => s.PlatformId == "KR").Status.Should().Be(MainCandidateStatus.Scored);
        }

        // The backlog settles over the next runs rather than in one unbounded statement.
        await process.RunCoreAsync(CancellationToken.None);
        await process.RunCoreAsync(CancellationToken.None);

        await using var finalDb = _fixture.CreateDbContext();
        (await finalDb.MainCandidates.AsNoTracking()
                .CountAsync(c => c.Status == MainCandidateStatus.Queued))
            .Should().Be(0);
    }

    [Fact]
    public async Task GetScoredByPlatformAsync_DoesNotOfferCandidatesOfIngestedAccounts()
    {
        var now = DateTime.UtcNow;

        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.AddRange(
                Account("ingested", "KR", now, lastMatchIngestAtUtc: now.AddDays(-3)),
                Account("never", "KR", now, lastMatchIngestAtUtc: null));
            db.MainCandidates.AddRange(
                // Highest score on purpose: it must lose its slot to the lower-scored unseen ones.
                Candidate("ingested", 1, MainCandidateStatus.Scored, now, score: 99, platformId: "KR"),
                Candidate("never", 2, MainCandidateStatus.Scored, now, score: 50, platformId: "KR"),
                Candidate("no-account", 3, MainCandidateStatus.Scored, now, score: 40, platformId: "KR"));
            await db.SaveChangesAsync();
        }

        await using var readDb = _fixture.CreateDbContext();
        var promotable = await new MainCandidateRepository(readDb)
            .GetScoredByPlatformAsync("KR", 10, [], CancellationToken.None);

        promotable.Select(c => c.Puuid).Should().Equal("never", "no-account");
    }

    private MatchDataRetentionProcess BuildProcess(IntakeOptions intake) => new(
        NullLogger<MatchDataRetentionProcess>.Instance,
        new TrueMain.TestKit.TestDbContextFactory(_fixture),
        _fixture.CreateSessionFactory(),
        TimeProvider.System,
        Microsoft.Extensions.Options.Options.Create(new MatchDataRetentionOptions { RetainedPatchCount = 2 }),
        Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions { QueueId = LolQueueId.RankedSoloDuo }),
        Microsoft.Extensions.Options.Options.Create(new CandidatePruningOptions { Enabled = false }),
        Microsoft.Extensions.Options.Options.Create(intake));

    private static RiotAccount Account(string puuid, string platformId, DateTime now, DateTime? lastMatchIngestAtUtc)
        => new()
        {
            Puuid = puuid,
            PlatformId = platformId,
            GameName = puuid,
            TagLine = platformId,
            SummonerId = $"sum-{puuid}",
            ProfileIconId = 1,
            SummonerLevel = 100,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            LastMatchIngestAtUtc = lastMatchIngestAtUtc,
            MatchIngestStatus = MatchIngestStatus.Idle
        };

    private static MainCandidate Candidate(
        string puuid,
        int championId,
        MainCandidateStatus status,
        DateTime now,
        double score = 0,
        string platformId = "KR") => new()
    {
        PlatformId = platformId,
        Puuid = puuid,
        ChampionId = championId,
        Status = status,
        Score = score,
        ScoredAtUtc = now.AddDays(-1),
        LastPlayTimeUtc = now.AddDays(-1),
        DiscoveredAtUtc = now.AddDays(-2)
    };
}
