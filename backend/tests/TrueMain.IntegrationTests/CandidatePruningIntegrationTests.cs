using Core.Lol.Map;
using Core.Options;
using Data.Entities;
using AwesomeAssertions;
using Ingestor.Options;
using Ingestor.Processes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class CandidatePruningIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public CandidatePruningIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunAsync_PrunesOnlyStaleNeverPromotedCandidates()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;
        var stale = now.AddDays(-60);
        var fresh = now.AddDays(-1);

        await using (var db = _fixture.CreateDbContext())
        {
            db.MainCandidates.AddRange(
                Candidate("stale-new", 1, MainCandidateStatus.New, stale),
                Candidate("stale-scored", 2, MainCandidateStatus.Scored, stale),
                Candidate("stale-rejected", 3, MainCandidateStatus.Rejected, stale),
                Candidate("stale-queued", 4, MainCandidateStatus.Queued, stale),
                Candidate("stale-validated", 5, MainCandidateStatus.Validated, stale, validatedAtUtc: stale),
                Candidate("fresh-new", 6, MainCandidateStatus.New, fresh));
            await db.SaveChangesAsync();
        }

        await BuildProcess(pruneAfterDays: 30).RunCoreAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        var remaining = await verifyDb.MainCandidates.AsNoTracking()
            .Select(c => c.Puuid)
            .OrderBy(p => p)
            .ToListAsync();

        // Stale New/Scored/Rejected (never promoted) are pruned; in-flight (Queued),
        // Validated, and the fresh candidate survive.
        remaining.Should().BeEquivalentTo("fresh-new", "stale-queued", "stale-validated");
    }

    [Fact]
    public async Task RunAsync_WhenPruningDisabled_KeepsStaleCandidates()
    {
        await _fixture.ResetDatabaseAsync();
        var stale = DateTime.UtcNow.AddDays(-60);

        await using (var db = _fixture.CreateDbContext())
        {
            db.MainCandidates.Add(Candidate("stale-new", 1, MainCandidateStatus.New, stale));
            await db.SaveChangesAsync();
        }

        await BuildProcess(pruneAfterDays: 30, enabled: false).RunCoreAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        (await verifyDb.MainCandidates.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_DemotesTheLowestScoredExcessOfAnOverDeepQueue()
    {
        await _fixture.ResetDatabaseAsync();
        var fresh = DateTime.UtcNow.AddDays(-1);

        await using (var db = _fixture.CreateDbContext())
        {
            // Four queued candidates against a cap of 2: the two lowest-scored ones are the
            // excess. The KR row is on another platform, whose own queue is within the cap —
            // depth is a per-platform question, like every other budget in the pipeline.
            db.MainCandidates.AddRange(
                Candidate("keep-high", 1, MainCandidateStatus.Queued, fresh, score: 90, platformId: "EUW1"),
                Candidate("keep-mid", 2, MainCandidateStatus.Queued, fresh, score: 80, platformId: "EUW1"),
                Candidate("demote-low", 3, MainCandidateStatus.Queued, fresh, score: 10, platformId: "EUW1"),
                Candidate("demote-lowest", 4, MainCandidateStatus.Queued, fresh, score: 5, platformId: "EUW1"),
                Candidate("other-platform", 5, MainCandidateStatus.Queued, fresh, score: 1));
            await db.SaveChangesAsync();
        }

        await BuildProcess(
                pruneAfterDays: 30,
                intake: new IntakeOptions
                {
                    MaxQueuedPerPlatform = 2,
                    QueueDepthDemotionBatchSize = 1,
                    MaxDemotionBatchesPerRun = 4
                })
            .RunCoreAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        var byPuuid = await verifyDb.MainCandidates.AsNoTracking()
            .ToDictionaryAsync(c => c.Puuid, c => c.Status);

        // Demoted, never deleted: the rows are still there, back in the promotion ranking.
        byPuuid.Should().HaveCount(5);
        byPuuid["keep-high"].Should().Be(MainCandidateStatus.Queued);
        byPuuid["keep-mid"].Should().Be(MainCandidateStatus.Queued);
        byPuuid["demote-low"].Should().Be(MainCandidateStatus.Scored);
        byPuuid["demote-lowest"].Should().Be(MainCandidateStatus.Scored);
        byPuuid["other-platform"].Should().Be(MainCandidateStatus.Queued);
    }

    private MatchDataRetentionProcess BuildProcess(
        int pruneAfterDays,
        bool enabled = true,
        IntakeOptions? intake = null) => new(
        NullLogger<MatchDataRetentionProcess>.Instance,
        new TrueMain.TestKit.TestDbContextFactory(_fixture),
        _fixture.CreateSessionFactory(),
        TimeProvider.System,
        Microsoft.Extensions.Options.Options.Create(new MatchDataRetentionOptions { RetainedPatchCount = 2 }),
        Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions { QueueId = LolQueueId.RankedSoloDuo }),
        Microsoft.Extensions.Options.Options.Create(new CandidatePruningOptions
        {
            Enabled = enabled,
            PruneAfterDays = pruneAfterDays
        }),
        Microsoft.Extensions.Options.Options.Create(intake ?? new IntakeOptions()));

    private static MainCandidate Candidate(
        string puuid,
        int championId,
        MainCandidateStatus status,
        DateTime lastPlayTimeUtc,
        DateTime? validatedAtUtc = null,
        double score = 0,
        string platformId = "KR") => new()
    {
        PlatformId = platformId,
        Puuid = puuid,
        ChampionId = championId,
        Status = status,
        Score = score,
        LastPlayTimeUtc = lastPlayTimeUtc,
        DiscoveredAtUtc = lastPlayTimeUtc,
        ValidatedAtUtc = validatedAtUtc
    };
}
