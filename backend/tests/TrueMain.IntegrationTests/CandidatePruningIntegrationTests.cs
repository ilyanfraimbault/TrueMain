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

    private MatchDataRetentionProcess BuildProcess(int pruneAfterDays, bool enabled = true) => new(
        NullLogger<MatchDataRetentionProcess>.Instance,
        new TrueMain.TestKit.TestDbContextFactory(_fixture),
        Microsoft.Extensions.Options.Options.Create(new MatchDataRetentionOptions { RetainedPatchCount = 2 }),
        Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions { QueueId = LolQueueId.RankedSoloDuo }),
        Microsoft.Extensions.Options.Options.Create(new CandidatePruningOptions
        {
            Enabled = enabled,
            PruneAfterDays = pruneAfterDays
        }));

    private static MainCandidate Candidate(
        string puuid,
        int championId,
        MainCandidateStatus status,
        DateTime lastPlayTimeUtc,
        DateTime? validatedAtUtc = null) => new()
    {
        PlatformId = "KR",
        Puuid = puuid,
        ChampionId = championId,
        Status = status,
        LastPlayTimeUtc = lastPlayTimeUtc,
        DiscoveredAtUtc = lastPlayTimeUtc,
        ValidatedAtUtc = validatedAtUtc
    };
}
