using AwesomeAssertions;
using Data.Entities;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.Coverage;
using Ingestor.Processes.Components.Discovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class HarvestProcessIntegrationTests
{
    private const int RankedSolo = 420;

    private readonly PostgresFixture _fixture;

    public HarvestProcessIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunAsync_CreatesHarvestCandidatesAndMinimalAccounts_FromOrphanParticipants()
    {
        await _fixture.ResetDatabaseAsync();
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

        await using (var db = _fixture.CreateDbContext())
        {
            // 6 orphan ranked-solo games on champ 22 for an untracked puuid, 4 wins.
            for (var i = 0; i < 6; i++)
            {
                MatchParticipantSeed.AddMatchWithParticipant(
                    db, $"H_{i}", "KR", RankedSolo, now.AddDays(-i), "harvest-puuid", 22, win: i < 4);
            }

            await db.SaveChangesAsync();
        }

        var process = new HarvestProcess(
            NullLogger<HarvestProcess>.Instance,
            _fixture.CreateSessionFactory(),
            new ParticipantHarvestService(),
            new ChampionCoverageProvider(
                Microsoft.Extensions.Options.Options.Create(new CoverageOptions())),
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new HarvestOptions
            {
                Platforms = ["KR"],
                QueueId = RankedSolo,
                MinObservedGames = 5,
                // Disable the date filter: this suite seeds at a fixed timestamp while the
                // process stamps the cutoff from the wall clock. The lookback is covered by
                // HarvestCandidatesQueryIntegrationTests.
                LookbackDays = 0
            }));

        await process.RunCoreAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();

        // Round-trips the new MainCandidate columns through EF — also guards against a
        // stale compiled model silently dropping Source/ObservedGames/ObservedWins.
        var candidate = await verifyDb.MainCandidates.AsNoTracking()
            .SingleAsync(c => c.Puuid == "harvest-puuid" && c.ChampionId == 22);
        candidate.Source.Should().Be(MainCandidateSource.Harvest);
        candidate.Status.Should().Be(MainCandidateStatus.New);
        candidate.ObservedGames.Should().Be(6);
        candidate.ObservedWins.Should().Be(4);

        // Match ingestion claims RiotAccount rows, so the harvested puuid gets a minimal
        // account with blank identity for AccountRefresh to backfill.
        var account = await verifyDb.RiotAccounts.AsNoTracking().SingleAsync(a => a.Puuid == "harvest-puuid");
        account.PlatformId.Should().Be("KR");
        account.GameName.Should().BeEmpty();
        account.MatchIngestStatus.Should().Be(MatchIngestStatus.Idle);
    }

    [Fact]
    public async Task RunAsync_WithMultipleSaveSlices_PersistsTheInPlaceCandidateUpdateOfEverySlice()
    {
        await _fixture.ResetDatabaseAsync();
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);
        var puuids = new[] { "harvest-slice-1", "harvest-slice-2" };

        await using (var db = _fixture.CreateDbContext())
        {
            foreach (var puuid in puuids)
            {
                for (var i = 0; i < 6; i++)
                {
                    MatchParticipantSeed.AddMatchWithParticipant(
                        db, $"H_{puuid}_{i}", "KR", RankedSolo, now.AddDays(-i), puuid, 22, win: i < 4);
                }

                // A candidate that already exists takes the *update-in-place* branch of
                // UpsertCandidate — the tracked entity whose writes a drained tracker would
                // swallow — rather than the insert branch a fresh puuid would take.
                db.MainCandidates.Add(new MainCandidate
                {
                    Id = Guid.NewGuid(),
                    Puuid = puuid,
                    PlatformId = "KR",
                    ChampionId = 22,
                    Source = MainCandidateSource.Harvest,
                    Status = MainCandidateStatus.New,
                    ObservedGames = 1,
                    ObservedWins = 0,
                    LastPlayTimeUtc = now.AddDays(-10),
                    DiscoveredAtUtc = now.AddDays(-10)
                });
            }

            await db.SaveChangesAsync();
        }

        var process = new HarvestProcess(
            NullLogger<HarvestProcess>.Instance,
            _fixture.CreateSessionFactory(),
            new ParticipantHarvestService(),
            new ChampionCoverageProvider(
                Microsoft.Extensions.Options.Options.Create(new CoverageOptions())),
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new HarvestOptions
            {
                Platforms = ["KR"],
                QueueId = RankedSolo,
                MinObservedGames = 5,
                LookbackDays = 0,
                // One row per slice, so the second candidate is preloaded and mutated after
                // the first slice's SaveChanges + ClearTracking (#1229). The preload sits
                // inside the slice precisely so that update is not written to a detached
                // entity and silently dropped; nothing here exercised more than one slice.
                SaveBatchSize = 1
            }));

        await process.RunCoreAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        foreach (var puuid in puuids)
        {
            var candidate = await verifyDb.MainCandidates.AsNoTracking()
                .SingleAsync(c => c.Puuid == puuid && c.ChampionId == 22);

            // Seeded at 1/0; the harvest observed 6 games and 4 wins for both.
            candidate.ObservedGames.Should().Be(6, "the update of slice {0} must survive the drain", puuid);
            candidate.ObservedWins.Should().Be(4);
        }
    }
}
