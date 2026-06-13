using AwesomeAssertions;
using Data;
using Data.Entities;
using Ingestor.Options;
using Ingestor.Processes;
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
                AddMatchWithParticipant(db, $"H_{i}", "KR", now.AddDays(-i), "harvest-puuid", 22, win: i < 4);
            }

            await db.SaveChangesAsync();
        }

        var process = new HarvestProcess(
            NullLogger<HarvestProcess>.Instance,
            _fixture.CreateSessionFactory(),
            new ParticipantHarvestService(),
            Microsoft.Extensions.Options.Options.Create(new HarvestOptions
            {
                Platforms = ["KR"],
                QueueId = RankedSolo,
                MinObservedGames = 5
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

    private static void AddMatchWithParticipant(
        TrueMainDbContext db,
        string matchId,
        string platformId,
        DateTime gameStartTimeUtc,
        string puuid,
        int championId,
        bool win)
    {
        db.Matches.Add(new Match
        {
            Id = matchId,
            PlatformId = platformId,
            QueueId = RankedSolo,
            MapId = 11,
            GameMode = "CLASSIC",
            GameType = "MATCHED_GAME",
            GameStartTimeUtc = gameStartTimeUtc,
            GameDurationSeconds = 1800,
            GameVersion = "16.6.1",
            CreatedAtUtc = gameStartTimeUtc,
            TimelineIngested = true
        });

        db.MatchParticipants.Add(new MatchParticipant
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            ParticipantId = 1,
            RiotAccountId = null,
            Puuid = puuid,
            SummonerName = puuid,
            SummonerLevel = 100,
            ChampionId = championId,
            TeamId = 100,
            TeamPosition = "BOTTOM",
            IndividualPosition = "BOTTOM",
            Lane = "BOTTOM",
            Role = "CARRY",
            Win = win,
            Kills = 1,
            Deaths = 1,
            Assists = 1,
            GoldEarned = 10000,
            TotalMinionsKilled = 100,
            NeutralMinionsKilled = 0,
            ChampLevel = 14,
            Item0 = 6672,
            Item1 = 3006,
            Item6 = 3363,
            TrinketItemId = 3363,
            PrimaryStyleId = 8000,
            SubStyleId = 8200,
            Summoner1Id = 4,
            Summoner2Id = 7,
            ItemEvents = [],
            SkillEvents = []
        });
    }
}
