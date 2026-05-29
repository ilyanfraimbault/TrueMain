using Data.Entities;
using Data.Repositories;
using AwesomeAssertions;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Riot.Dto;

namespace TrueMain.UnitTests;

/// <summary>
/// Sister of <see cref="RiotMatchMapperPerkSelectionTests"/>: covers the main
/// Map() entry point that turns a Riot DTO into the persisted Match aggregate
/// + participants. Lock the mapping so that schema-driven assumptions
/// (timestamp conversion, primary/sub style detection, riot account
/// correlation, trinket = item6 alias) survive future Riot API changes.
/// </summary>
public sealed class RiotMatchMapperTests
{
    private const string TestPlatform = "KR";
    private const string TestMatchId = "KR_1234567890";

    [Fact]
    public void Map_TranslatesMatchHeaderFields()
    {
        var dto = BuildMatch(gameVersion: "16.4.1", queueId: 420, mapId: 11, gameDuration: 1800,
            startTimestampMs: 1577836800000); // 2020-01-01 00:00:00 UTC

        var result = RiotMatchMapper.Map(dto, TestPlatform, EmptyAccountMap());

        result.Match.Id.Should().Be(TestMatchId);
        result.Match.PlatformId.Should().Be(TestPlatform);
        result.Match.QueueId.Should().Be(420);
        result.Match.MapId.Should().Be(11);
        result.Match.GameDurationSeconds.Should().Be(1800);
        result.Match.GameVersion.Should().Be("16.4.1");
        result.Match.GameStartTimeUtc.Should().Be(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        result.Match.TimelineIngested.Should().BeFalse();
    }

    [Fact]
    public void Map_FallsBackToUtcNow_WhenStartTimestampIsZero()
    {
        var dto = BuildMatch(startTimestampMs: 0);
        var before = DateTime.UtcNow;

        var result = RiotMatchMapper.Map(dto, TestPlatform, EmptyAccountMap());

        result.Match.GameStartTimeUtc.Should().BeOnOrAfter(before);
        result.Match.GameStartTimeUtc.Should().BeOnOrBefore(DateTime.UtcNow);
    }

    [Fact]
    public void Map_DetectsPrimaryAndSubStyleFromPerkDescriptions()
    {
        var dto = BuildMatch();
        dto.Info.Participants.Add(BuildParticipant(participantId: 1, puuid: "p-1",
            primaryStyleId: 8000, subStyleId: 8400));

        var result = RiotMatchMapper.Map(dto, TestPlatform, EmptyAccountMap());

        result.Participants.Should().ContainSingle();
        var participant = result.Participants[0];
        participant.PrimaryStyleId.Should().Be(8000);
        participant.SubStyleId.Should().Be(8400);
    }

    [Fact]
    public void Map_DefaultsBothStyles_WhenPerkStylesMissing()
    {
        var dto = BuildMatch();
        var participantDto = BuildParticipant(participantId: 1, puuid: "p-1");
        participantDto.Perks.Styles.Clear();
        dto.Info.Participants.Add(participantDto);

        var result = RiotMatchMapper.Map(dto, TestPlatform, EmptyAccountMap());

        result.Participants[0].PrimaryStyleId.Should().Be(0);
        result.Participants[0].SubStyleId.Should().Be(0);
    }

    [Fact]
    public void Map_AliasesTrinketAsItem6()
    {
        var dto = BuildMatch();
        var participantDto = BuildParticipant(participantId: 1, puuid: "p-1");
        participantDto.Item6 = 3340;
        dto.Info.Participants.Add(participantDto);

        var result = RiotMatchMapper.Map(dto, TestPlatform, EmptyAccountMap());

        result.Participants[0].Item6.Should().Be(3340);
        result.Participants[0].TrinketItemId.Should().Be(3340);
    }

    [Fact]
    public void Map_AssignsRiotAccountId_WhenParticipantPuuidMatchesPlatformAndPuuid()
    {
        var accountId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var dto = BuildMatch();
        dto.Info.Participants.Add(BuildParticipant(participantId: 1, puuid: "matched-puuid"));
        dto.Info.Participants.Add(BuildParticipant(participantId: 2, puuid: "other-puuid"));

        var accounts = new Dictionary<AccountKey, RiotAccount>
        {
            [new AccountKey(TestPlatform, "matched-puuid")] = new()
            {
                Id = accountId,
                PlatformId = TestPlatform,
                Puuid = "matched-puuid"
            }
        };

        var result = RiotMatchMapper.Map(dto, TestPlatform, accounts);

        result.Participants.Should().HaveCount(2);
        result.Participants.Single(p => p.Puuid == "matched-puuid").RiotAccountId.Should().Be(accountId);
        result.Participants.Single(p => p.Puuid == "other-puuid").RiotAccountId.Should().BeNull();
    }

    [Fact]
    public void Map_LeavesItemAndSkillEventsEmpty_AsTimelineHydratesThemSeparately()
    {
        var dto = BuildMatch();
        dto.Info.Participants.Add(BuildParticipant(participantId: 1, puuid: "p-1"));

        var result = RiotMatchMapper.Map(dto, TestPlatform, EmptyAccountMap());

        result.Participants[0].ItemEvents.Should().BeEmpty();
        result.Participants[0].SkillEvents.Should().BeEmpty();
    }

    [Fact]
    public void Map_PreservesParticipantOrderAndCount()
    {
        var dto = BuildMatch();
        for (var i = 1; i <= 10; i++)
        {
            dto.Info.Participants.Add(BuildParticipant(participantId: i, puuid: $"puuid-{i}"));
        }

        var result = RiotMatchMapper.Map(dto, TestPlatform, EmptyAccountMap());

        result.Participants.Should().HaveCount(10);
        result.Participants.Select(p => p.ParticipantId).Should().BeInAscendingOrder();
    }

    private static IReadOnlyDictionary<AccountKey, RiotAccount> EmptyAccountMap()
        => new Dictionary<AccountKey, RiotAccount>();

    private static RiotMatchDto BuildMatch(
        string gameVersion = "16.4.1",
        int queueId = 420,
        int mapId = 11,
        long gameDuration = 1800,
        long startTimestampMs = 1739000000000)
    {
        return new RiotMatchDto
        {
            Metadata = new RiotMatchMetadataDto { MatchId = TestMatchId },
            Info = new RiotMatchInfoDto
            {
                QueueId = queueId,
                MapId = mapId,
                GameMode = "CLASSIC",
                GameType = "MATCHED_GAME",
                GameStartTimestamp = startTimestampMs,
                GameDuration = gameDuration,
                GameVersion = gameVersion,
                Participants = []
            }
        };
    }

    private static RiotParticipantDto BuildParticipant(
        int participantId,
        string puuid,
        int primaryStyleId = 0,
        int subStyleId = 0)
    {
        return new RiotParticipantDto
        {
            ParticipantId = participantId,
            Puuid = puuid,
            ChampionId = 22,
            TeamId = 100,
            TeamPosition = "BOTTOM",
            IndividualPosition = "BOTTOM",
            Lane = "BOTTOM",
            Role = "CARRY",
            Win = true,
            Perks = new RiotPerksDto
            {
                StatPerks = new RiotStatPerksDto(),
                Styles =
                [
                    new RiotPerkStyleDto { Style = primaryStyleId, Description = "primaryStyle" },
                    new RiotPerkStyleDto { Style = subStyleId, Description = "subStyle" }
                ]
            }
        };
    }
}
