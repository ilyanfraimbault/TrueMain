using Core.Lol.Map;
using Core.Lol.Identifiers;
using Data.Entities;
using AwesomeAssertions;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Riot;
using Ingestor.Riot.Dto;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class MatchSnapshotWriterIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public MatchSnapshotWriterIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task IngestSnapshotsAsync_ShouldPersistRawMatchParticipantsAndPerks()
    {
        await _fixture.ResetDatabaseAsync();

        await using var session = await _fixture.CreateSessionFactory().CreateAsync(CancellationToken.None);
        var service = new MatchSnapshotWriter(new FakeRiotMatchClient());

        var result = await service.IngestSnapshotsAsync(
            session,
            "KR",
            "puuid-1",
            RegionalRoute.Asia,
            matchesPerAccount: 10,
            saveBatchSize: 10,
            CancellationToken.None);

        result.Inserted.Should().Be(1);
        result.Skipped.Should().Be(0);
        result.AllMatchIds.Should().ContainSingle().Which.Should().Be("KR_100");

        await using var verifyDb = _fixture.CreateDbContext();
        var match = verifyDb.Matches.Single(m => m.Id == "KR_100");
        var participants = verifyDb.MatchParticipants.Where(p => p.MatchId == "KR_100").OrderBy(p => p.ParticipantId).ToList();
        var perkLinks = verifyDb.ParticipantPerkSelections.Where(p => p.MatchId == "KR_100").ToList();
        var catalog = verifyDb.PerkSelectionCatalogs.ToList();

        match.PlatformId.Should().Be("KR");
        match.QueueId.Should().Be(420);
        match.GameVersion.Should().Be("16.4.1");

        participants.Should().HaveCount(2);
        participants[0].Puuid.Should().Be("puuid-1");
        participants[0].ChampionId.Should().Be(22);
        participants[0].Summoner1Id.Should().Be(4);
        participants[0].Summoner2Id.Should().Be(7);
        participants[0].ItemEvents.Should().BeEmpty();
        participants[0].SkillEvents.Should().BeEmpty();
        participants[1].Puuid.Should().Be("puuid-2");
        participants[1].ChampionId.Should().Be(51);
        participants[1].Lane.Should().Be("TOP");
        participants[1].Role.Should().Be("SOLO");
        participants[1].Summoner1Id.Should().Be(14);
        participants[1].Summoner2Id.Should().Be(4);
        participants[1].PrimaryStyleId.Should().Be(8400);
        participants[1].SubStyleId.Should().Be(8300);
        participants[1].PerksDefense.Should().Be(5002);
        participants[1].PerksFlex.Should().Be(5003);
        participants[1].PerksOffense.Should().Be(5007);
        participants[1].ItemEvents.Should().BeEmpty();
        participants[1].SkillEvents.Should().BeEmpty();

        perkLinks.Should().HaveCount(12);
        catalog.Should().HaveCount(12);
    }

    [Fact]
    public async Task IngestSnapshotsAsync_ShouldSkipAlreadyPersistedMatches()
    {
        await _fixture.ResetDatabaseAsync();
        var service = new MatchSnapshotWriter(new FakeRiotMatchClient());

        await using (var firstSession = await _fixture.CreateSessionFactory().CreateAsync(CancellationToken.None))
        {
            var first = await service.IngestSnapshotsAsync(
                firstSession,
                "KR",
                "puuid-1",
                RegionalRoute.Asia,
                matchesPerAccount: 10,
                saveBatchSize: 10,
                CancellationToken.None);

            first.Inserted.Should().Be(1);
        }

        await using var secondSession = await _fixture.CreateSessionFactory().CreateAsync(CancellationToken.None);
        var second = await service.IngestSnapshotsAsync(
            secondSession,
            "KR",
            "puuid-1",
            RegionalRoute.Asia,
            matchesPerAccount: 10,
            saveBatchSize: 10,
            CancellationToken.None);

        second.Inserted.Should().Be(0);
        second.Skipped.Should().Be(1);

        await using var verifyDb = _fixture.CreateDbContext();
        verifyDb.Matches.Should().ContainSingle();
        verifyDb.MatchParticipants.Should().HaveCount(2);
    }

    [Fact]
    public async Task IngestSnapshotsAsync_ShouldBackfillTrackedRiotAccountIdForExistingMatches()
    {
        await _fixture.ResetDatabaseAsync();
        var service = new MatchSnapshotWriter(new FakeRiotMatchClient());
        var now = DateTime.UtcNow;

        await using (var seedDb = _fixture.CreateDbContext())
        {
            seedDb.RiotAccounts.AddRange(
                new RiotAccount
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    PlatformId = "KR",
                    Puuid = "puuid-1",
                    GameName = "player-one",
                    SummonerId = "player-one-summoner",
                    ProfileIconId = 1,
                    SummonerLevel = 100,
                    LastProfileSyncAtUtc = now,
                    CreatedAtUtc = now.AddDays(-10),
                    UpdatedAtUtc = now.AddDays(-1)
                },
                new RiotAccount
                {
                    Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    PlatformId = "KR",
                    Puuid = "puuid-2",
                    GameName = "player-two",
                    SummonerId = "player-two-summoner",
                    ProfileIconId = 1,
                    SummonerLevel = 100,
                    LastProfileSyncAtUtc = now,
                    CreatedAtUtc = now.AddDays(-10),
                    UpdatedAtUtc = now.AddDays(-1)
                });
            await seedDb.SaveChangesAsync();
        }

        await using (var firstSession = await _fixture.CreateSessionFactory().CreateAsync(CancellationToken.None))
        {
            var first = await service.IngestSnapshotsAsync(
                firstSession,
                "KR",
                "puuid-1",
                RegionalRoute.Asia,
                matchesPerAccount: 10,
                saveBatchSize: 10,
                CancellationToken.None);

            first.Inserted.Should().Be(1);
        }

        await using (var secondSession = await _fixture.CreateSessionFactory().CreateAsync(CancellationToken.None))
        {
            var second = await service.IngestSnapshotsAsync(
                secondSession,
                "KR",
                "puuid-2",
                RegionalRoute.Asia,
                matchesPerAccount: 10,
                saveBatchSize: 10,
                CancellationToken.None);

            second.Inserted.Should().Be(0);
            second.Skipped.Should().Be(1);
        }

        await using var verifyDb = _fixture.CreateDbContext();
        var participants = verifyDb.MatchParticipants
            .Where(participant => participant.MatchId == "KR_100")
            .OrderBy(participant => participant.ParticipantId)
            .ToList();

        participants[0].RiotAccountId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        participants[1].RiotAccountId.Should().Be(Guid.Parse("22222222-2222-2222-2222-222222222222"));
    }

    [Fact]
    public async Task IngestSnapshotsAsync_ShouldAssignKnownRiotAccountIdsForAllKnownParticipantsInANewMatch()
    {
        await _fixture.ResetDatabaseAsync();
        var service = new MatchSnapshotWriter(new FakeRiotMatchClient());
        var now = DateTime.UtcNow;

        await using (var seedDb = _fixture.CreateDbContext())
        {
            seedDb.RiotAccounts.AddRange(
                new RiotAccount
                {
                    Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    PlatformId = "KR",
                    Puuid = "puuid-1",
                    GameName = "player-one",
                    SummonerId = "player-one-summoner",
                    ProfileIconId = 1,
                    SummonerLevel = 100,
                    LastProfileSyncAtUtc = now,
                    CreatedAtUtc = now.AddDays(-10),
                    UpdatedAtUtc = now.AddDays(-1)
                },
                new RiotAccount
                {
                    Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                    PlatformId = "KR",
                    Puuid = "puuid-2",
                    GameName = "player-two",
                    SummonerId = "player-two-summoner",
                    ProfileIconId = 1,
                    SummonerLevel = 100,
                    LastProfileSyncAtUtc = now,
                    CreatedAtUtc = now.AddDays(-10),
                    UpdatedAtUtc = now.AddDays(-1)
                });
            await seedDb.SaveChangesAsync();
        }

        await using (var session = await _fixture.CreateSessionFactory().CreateAsync(CancellationToken.None))
        {
            var result = await service.IngestSnapshotsAsync(
                session,
                "KR",
                "puuid-1",
                RegionalRoute.Asia,
                matchesPerAccount: 10,
                saveBatchSize: 10,
                CancellationToken.None);

            result.Inserted.Should().Be(1);
        }

        await using var verifyDb = _fixture.CreateDbContext();
        var participants = verifyDb.MatchParticipants
            .Where(participant => participant.MatchId == "KR_100")
            .OrderBy(participant => participant.ParticipantId)
            .ToList();

        participants[0].RiotAccountId.Should().Be(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        participants[1].RiotAccountId.Should().Be(Guid.Parse("44444444-4444-4444-4444-444444444444"));
    }

    private sealed class FakeRiotMatchClient : IRiotMatchClient
    {
        public Task<List<string>> GetMatchIdsAsync(string puuid, RegionalRoute region, int count, CancellationToken ct)
            => Task.FromResult(new List<string> { "KR_100" });

        public Task<RiotMatchDto> GetMatchAsync(string matchId, RegionalRoute region, CancellationToken ct)
        {
            return Task.FromResult(new RiotMatchDto
            {
                Metadata = new RiotMatchMetadataDto
                {
                    MatchId = matchId
                },
                Info = new RiotMatchInfoDto
                {
                    QueueId = (int)LolQueueId.RankedSoloDuo,
                    MapId = (int)LolMapId.SummonersRift,
                    GameMode = "CLASSIC",
                    GameType = "MATCHED_GAME",
                    GameStartTimestamp = new DateTimeOffset(2026, 3, 10, 20, 15, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(),
                    GameDuration = 1800,
                    GameVersion = "16.4.1",
                    Participants =
                    [
                        CreateParticipant(1, "puuid-1", "player-one", 22, true, "BOTTOM", "DUO_CARRY", 4, 7),
                        CreateParticipant(2, "puuid-2", "player-two", 51, false, "TOP", "SOLO", 14, 4)
                    ]
                }
            });
        }

        public Task<MatchTimelineDto> GetTimelineAsync(string matchId, RegionalRoute region, CancellationToken ct)
            => throw new NotSupportedException();

        private static RiotParticipantDto CreateParticipant(
            int participantId,
            string puuid,
            string summonerName,
            int championId,
            bool win,
            string lane,
            string role,
            int summoner1Id,
            int summoner2Id)
        {
            return new RiotParticipantDto
            {
                ParticipantId = participantId,
                Puuid = puuid,
                SummonerName = summonerName,
                SummonerLevel = participantId == 1 ? 250 : 180,
                ChampionId = championId,
                TeamId = participantId == 1 ? 100 : 200,
                TeamPosition = lane,
                IndividualPosition = lane,
                Lane = lane,
                Role = role,
                Win = win,
                Kills = participantId == 1 ? 8 : 4,
                Deaths = participantId == 1 ? 2 : 6,
                Assists = participantId == 1 ? 10 : 3,
                GoldEarned = participantId == 1 ? 14500 : 9900,
                TotalMinionsKilled = participantId == 1 ? 220 : 165,
                NeutralMinionsKilled = participantId == 1 ? 12 : 8,
                ChampLevel = participantId == 1 ? 16 : 14,
                Item0 = participantId == 1 ? 6672 : 1055,
                Item1 = participantId == 1 ? 3006 : 3047,
                Item2 = participantId == 1 ? 3085 : 3158,
                Item3 = participantId == 1 ? 3031 : 3071,
                Item4 = participantId == 1 ? 3036 : 3111,
                Item5 = participantId == 1 ? 3094 : 3068,
                Item6 = 3363,
                Summoner1Id = summoner1Id,
                Summoner2Id = summoner2Id,
                Perks = new RiotPerksDto
                {
                    StatPerks = new RiotStatPerksDto
                    {
                        Defense = participantId == 1 ? 5001 : 5002,
                        Flex = participantId == 1 ? 5008 : 5003,
                        Offense = participantId == 1 ? 5005 : 5007
                    },
                    Styles =
                    [
                        new RiotPerkStyleDto
                        {
                            Style = participantId == 1 ? 8000 : 8400,
                            Description = "primaryStyle",
                            Selections =
                            [
                                new RiotPerkSelectionDto { Perk = participantId == 1 ? 8005 : 8439 },
                                new RiotPerkSelectionDto { Perk = participantId == 1 ? 9111 : 8446 },
                                new RiotPerkSelectionDto { Perk = participantId == 1 ? 9104 : 8429 },
                                new RiotPerkSelectionDto { Perk = participantId == 1 ? 8014 : 8451 }
                            ]
                        },
                        new RiotPerkStyleDto
                        {
                            Style = participantId == 1 ? 8100 : 8300,
                            Description = "subStyle",
                            Selections =
                            [
                                new RiotPerkSelectionDto { Perk = participantId == 1 ? 8139 : 8345 },
                                new RiotPerkSelectionDto { Perk = participantId == 1 ? 8135 : 8347 }
                            ]
                        }
                    ]
                }
            };
        }
    }
}
