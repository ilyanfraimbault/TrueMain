using Core.Lol.Map;
using Core.Lol.Identifiers;
using Data.Entities;
using Data.Repositories;
using AwesomeAssertions;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Riot;
using Ingestor.Riot.Dto;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class MatchTimelineRecoveryIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public MatchTimelineRecoveryIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TimelineIngestionService_ShouldRepairPendingTimelineAndMarkMatchAsIngested()
    {
        await _fixture.ResetDatabaseAsync();
        const string matchId = "KR_100";

        await SeedPendingMatchAsync(matchId);

        await using var db = _fixture.CreateDbContext();
        await using var session = new DataSession(db);
        var service = new TimelineIngestionService(new FakeRiotMatchClient());

        var updated = await service.IngestTimelinesAsync(
            session,
            RegionalRoute.Asia,
            new[] { matchId },
            Array.Empty<string>(),
            saveBatchSize: 10,
            CancellationToken.None);

        updated.Should().Be(1);

        await using var verifyDb = _fixture.CreateDbContext();
        var participant = verifyDb.MatchParticipants.Single(p => p.MatchId == matchId && p.ParticipantId == 1);
        participant.ItemEvents.Should().ContainSingle();
        participant.SkillEvents.Should().ContainSingle();
        participant.ItemEvents[0].ItemId.Should().Be(1055);
        participant.ItemEvents[0].EventType.Should().Be("ITEM_PURCHASED");

        var match = verifyDb.Matches.Single(m => m.Id == matchId);
        match.TimelineIngested.Should().BeTrue();
    }

    private async Task SeedPendingMatchAsync(string matchId)
    {
        await using var db = _fixture.CreateDbContext();
        var now = DateTime.UtcNow;

        db.Matches.Add(new Match
        {
            Id = matchId,
            PlatformId = "KR",
            QueueId = (int)LolQueueId.RankedSoloDuo,
            MapId = (int)LolMapId.SummonersRift,
            GameMode = "CLASSIC",
            GameType = "MATCHED_GAME",
            GameStartTimeUtc = now,
            GameDurationSeconds = 1800,
            GameVersion = "14.1.1",
            CreatedAtUtc = now,
            TimelineIngested = false
        });

        db.MatchParticipants.Add(new MatchParticipant
        {
            MatchId = matchId,
            ParticipantId = 1,
            Puuid = "puuid-1",
            SummonerName = "player-1",
            SummonerLevel = 100,
            ChampionId = 10,
            TeamId = 100,
            TeamPosition = "TOP",
            IndividualPosition = "TOP",
            Lane = "TOP",
            Role = "SOLO",
            Win = true,
            Kills = 1,
            Deaths = 1,
            Assists = 1,
            GoldEarned = 1000,
            TotalMinionsKilled = 100,
            NeutralMinionsKilled = 10,
            ChampLevel = 14,
            Item0 = 1055,
            Item1 = 0,
            Item2 = 0,
            Item3 = 0,
            Item4 = 0,
            Item5 = 0,
            Item6 = 3363,
            TrinketItemId = 3363,
            PerksDefense = 5001,
            PerksFlex = 5008,
            PerksOffense = 5005,
            PrimaryStyleId = 8000,
            SubStyleId = 8100,
            Summoner1Id = 4,
            Summoner2Id = 12,
            ItemEvents = [],
            SkillEvents = []
        });

        await db.SaveChangesAsync();
    }

    private sealed class FakeRiotMatchClient : IRiotMatchClient
    {
        public Task<RiotMatchDto> GetMatchAsync(string matchId, RegionalRoute region, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<List<string>> GetMatchIdsAsync(string puuid, RegionalRoute region, int count, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<MatchTimelineDto> GetTimelineAsync(string matchId, RegionalRoute region, CancellationToken ct)
        {
            return Task.FromResult(new MatchTimelineDto
            {
                Events =
                [
                    new MatchTimelineEventDto
                    {
                        ParticipantId = 1,
                        TimestampMs = 500,
                        Type = "ITEM_PURCHASED",
                        ItemId = 1055
                    },
                    new MatchTimelineEventDto
                    {
                        ParticipantId = 1,
                        TimestampMs = 1000,
                        Type = "SKILL_LEVEL_UP",
                        SkillSlot = 1,
                        LevelUpType = "NORMAL"
                    }
                ]
            });
        }
    }
}
