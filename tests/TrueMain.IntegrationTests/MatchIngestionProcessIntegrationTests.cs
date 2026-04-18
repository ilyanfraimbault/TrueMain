using Core.Lol.Map;
using Core.Lol.Identifiers;
using FluentAssertions;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using Ingestor.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace TrueMain.IntegrationTests;

public sealed class MatchIngestionProcessIntegrationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public MatchIngestionProcessIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunAsync_ShouldPersistRawMatchesForClaimedAccounts()
    {
        await _fixture.ResetDatabaseAsync();
        var validationService = new FakeAccountValidationService();
        var process = new MatchIngestionProcess(
            NullLogger<MatchIngestionProcess>.Instance,
            _fixture.CreateSessionFactory(),
            new FakeProcessRunRecorder(),
            new FakeMatchClaimService(),
            new MatchSnapshotWriter(new FakeRiotMatchClient()),
            new TimelineIngestionService(new FakeRiotMatchClient()),
            validationService,
            Microsoft.Extensions.Options.Options.Create(new MatchIngestionOptions
            {
                Platforms = ["KR"],
                BatchSize = 1,
                MatchesPerAccount = 5,
                SaveBatchSizeMatches = 10,
                ClaimLeaseMinutes = 5
            }));

        await process.RunAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        var match = verifyDb.Matches.Single(m => m.Id == "KR_200");
        var participants = verifyDb.MatchParticipants.Where(p => p.MatchId == "KR_200").OrderBy(p => p.ParticipantId).ToList();

        match.TimelineIngested.Should().BeTrue();
        participants.Should().HaveCount(2);
        participants[0].ItemEvents.Should().ContainSingle(e => e.ItemId == 6672 && e.EventType == "ITEM_PURCHASED");
        participants[0].SkillEvents.Should().ContainSingle(e => e.SkillSlot == 1);
        participants[1].ItemEvents.Should().ContainSingle(e => e.ItemId == 1055 && e.EventType == "ITEM_PURCHASED");
        participants[1].SkillEvents.Should().ContainSingle(e => e.SkillSlot == 2);
        validationService.Validated.Should().ContainSingle(key => key == new Data.Repositories.AccountKey("KR", "puuid-claimed-1"));
        validationService.Reverted.Should().BeEmpty();
    }

    private sealed class FakeMatchClaimService : IMatchClaimService
    {
        public Task<List<Data.Repositories.AccountKey>> ClaimAsync(
            IReadOnlyCollection<string> platforms,
            int batchSize,
            TimeSpan lease,
            CancellationToken ct)
            => Task.FromResult(new List<Data.Repositories.AccountKey>
            {
                new("KR", "puuid-claimed-1")
            });
    }

    private sealed class FakeAccountValidationService : IAccountValidationService
    {
        public List<Data.Repositories.AccountKey> Validated { get; } = [];
        public List<Data.Repositories.AccountKey> Reverted { get; } = [];

        public Task ValidateAsync(Data.Repositories.AccountKey account, CancellationToken ct)
        {
            Validated.Add(account);
            return Task.CompletedTask;
        }

        public Task RevertAsync(Data.Repositories.AccountKey account, CancellationToken ct)
        {
            Reverted.Add(account);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProcessRunRecorder : IProcessRunRecorder
    {
        public Task RecordAsync(
            string processName,
            DateTime startedAtUtc,
            DateTime finishedAtUtc,
            Data.Entities.ProcessRunStatus status,
            object? summary,
            string? error,
            CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class FakeRiotMatchClient : IRiotMatchClient
    {
        public Task<List<string>> GetMatchIdsAsync(string puuid, RegionalRoute region, int count, CancellationToken ct)
            => Task.FromResult(new List<string> { "KR_200" });

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
                    QueueId = LolQueueIds.RankedSoloDuo,
                    MapId = LolMapIds.SummonersRift,
                    GameMode = "CLASSIC",
                    GameType = "MATCHED_GAME",
                    GameStartTimestamp = new DateTimeOffset(2026, 3, 10, 21, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(),
                    GameDuration = 1920,
                    GameVersion = "16.4.1",
                    Participants =
                    [
                        CreateParticipant(1, "puuid-claimed-1", "claimed-player", 22, true, 4, 7, "BOTTOM", "DUO_CARRY", 6672),
                        CreateParticipant(2, "puuid-claimed-2", "enemy-player", 51, false, 4, 3, "TOP", "SOLO", 1055)
                    ]
                }
            });
        }

        public Task<MatchTimelineDto> GetTimelineAsync(string matchId, RegionalRoute region, CancellationToken ct)
        {
            return Task.FromResult(new MatchTimelineDto
            {
                Events =
                [
                    new MatchTimelineEventDto
                    {
                        ParticipantId = 1,
                        TimestampMs = 400,
                        Type = "ITEM_PURCHASED",
                        ItemId = 6672
                    },
                    new MatchTimelineEventDto
                    {
                        ParticipantId = 1,
                        TimestampMs = 800,
                        Type = "SKILL_LEVEL_UP",
                        SkillSlot = 1,
                        LevelUpType = "NORMAL"
                    },
                    new MatchTimelineEventDto
                    {
                        ParticipantId = 2,
                        TimestampMs = 500,
                        Type = "ITEM_PURCHASED",
                        ItemId = 1055
                    },
                    new MatchTimelineEventDto
                    {
                        ParticipantId = 2,
                        TimestampMs = 900,
                        Type = "SKILL_LEVEL_UP",
                        SkillSlot = 2,
                        LevelUpType = "NORMAL"
                    }
                ]
            });
        }

        private static RiotParticipantDto CreateParticipant(
            int participantId,
            string puuid,
            string summonerName,
            int championId,
            bool win,
            int summoner1Id,
            int summoner2Id,
            string lane,
            string role,
            int firstItem)
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
                Kills = participantId == 1 ? 8 : 3,
                Deaths = participantId == 1 ? 2 : 6,
                Assists = participantId == 1 ? 10 : 4,
                GoldEarned = participantId == 1 ? 14500 : 9800,
                TotalMinionsKilled = participantId == 1 ? 220 : 156,
                NeutralMinionsKilled = participantId == 1 ? 12 : 4,
                ChampLevel = participantId == 1 ? 16 : 14,
                Item0 = firstItem,
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
