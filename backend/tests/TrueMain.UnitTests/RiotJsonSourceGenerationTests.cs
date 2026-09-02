using System.Net;
using System.Text;
using AwesomeAssertions;
using Core.Lol.Identifiers;
using Ingestor.Riot;
using Ingestor.Riot.Dto;

namespace TrueMain.UnitTests;

/// <summary>
/// Riot deserialization moved from the reflection-based resolver to the
/// source-generated <c>RiotJsonContext</c> (#268). The resolver is source-gen
/// only, so a root type the clients ask for that is missing from the context
/// throws <see cref="NotSupportedException"/> at runtime instead of quietly
/// falling back — these tests drive every client method through a fake handler
/// with a realistic Riot body so a missing registration fails the build.
/// </summary>
public sealed class RiotJsonSourceGenerationTests
{
    // Trimmed to the fields the DTO maps plus a few Riot also sends and we
    // ignore (challenges, gameName/riotIdTagline, participant extras), so the
    // test also pins that unmapped members stay harmless.
    private const string MatchPayload = """
    {
      "metadata": { "dataVersion": "2", "matchId": "KR_7412001234", "participants": ["puuid-a", "puuid-b"] },
      "info": {
        "gameCreation": 1751200000000,
        "gameStartTimestamp": 1751200060000,
        "gameEndTimestamp": 1751201860000,
        "gameDuration": 1800,
        "gameId": 7412001234,
        "gameMode": "CLASSIC",
        "gameName": "teambuilder-match-7412001234",
        "gameType": "MATCHED_GAME",
        "gameVersion": "16.13.703.1234",
        "mapId": 11,
        "platformId": "KR",
        "queueId": 420,
        "participants": [
          {
            "participantId": 1,
            "puuid": "puuid-a",
            "riotIdGameName": "Phantasm",
            "riotIdTagline": "KR1",
            "summonerName": "Phantasm",
            "summonerLevel": 412,
            "championId": 157,
            "championName": "Yasuo",
            "teamId": 100,
            "teamPosition": "MIDDLE",
            "individualPosition": "MIDDLE",
            "lane": "MIDDLE",
            "role": "SOLO",
            "win": true,
            "kills": 12,
            "deaths": 3,
            "assists": 7,
            "totalDamageDealtToChampions": 28450,
            "visionScore": 21,
            "goldEarned": 15320,
            "totalMinionsKilled": 245,
            "neutralMinionsKilled": 12,
            "champLevel": 17,
            "item0": 6673,
            "item1": 3006,
            "item2": 3031,
            "item3": 3072,
            "item4": 3033,
            "item5": 0,
            "item6": 3363,
            "summoner1Id": 4,
            "summoner2Id": 14,
            "challenges": { "kda": 6.33, "soloKills": 4 },
            "perks": {
              "statPerks": { "defense": 5011, "flex": 5008, "offense": 5005 },
              "styles": [
                {
                  "description": "primaryStyle",
                  "selections": [
                    { "perk": 8008, "var1": 1, "var2": 0, "var3": 0 },
                    { "perk": 9111, "var1": 2, "var2": 0, "var3": 0 }
                  ],
                  "style": 8000
                },
                {
                  "description": "subStyle",
                  "selections": [{ "perk": 8139, "var1": 0, "var2": 0, "var3": 0 }],
                  "style": 8100
                }
              ]
            }
          }
        ],
        "teams": [{ "teamId": 100, "win": true }]
      }
    }
    """;

    private const string TimelinePayload = """
    {
      "metadata": { "matchId": "KR_7412001234" },
      "info": {
        "frameInterval": 60000,
        "frames": [
          {
            "timestamp": 300000,
            "events": [
              { "type": "ITEM_PURCHASED", "timestamp": 121000, "participantId": 1, "itemId": 1055 },
              {
                "type": "CHAMPION_KILL",
                "timestamp": 298000,
                "killerId": 1,
                "victimId": 6,
                "assistingParticipantIds": [2, 3],
                "position": { "x": 7500, "y": 7200 }
              },
              { "type": "SKILL_LEVEL_UP", "timestamp": 90000, "participantId": 1, "skillSlot": 1, "levelUpType": "NORMAL" }
            ],
            "participantFrames": {
              "1": {
                "participantId": 1,
                "currentGold": 420,
                "totalGold": 2450,
                "level": 6,
                "xp": 3100,
                "minionsKilled": 44,
                "jungleMinionsKilled": 2,
                "position": { "x": 7400, "y": 7100 },
                "damageStats": {
                  "totalDamageDoneToChampions": 3120,
                  "magicDamageDoneToChampions": 100,
                  "physicalDamageDoneToChampions": 2900,
                  "trueDamageDoneToChampions": 120
                }
              }
            }
          }
        ]
      }
    }
    """;

    [Fact]
    public async Task GetMatchAsync_DeserialisesARealisticMatchPayloadThroughTheGeneratedContext()
    {
        using var handler = new StubHandler(MatchPayload);
        using var httpClient = new HttpClient(handler);
        var client = new RiotMatchClient(httpClient);

        var match = await client.GetMatchAsync("KR_7412001234", RegionalRoute.Asia, CancellationToken.None);

        match.Metadata.MatchId.Should().Be("KR_7412001234");
        match.Info.QueueId.Should().Be(420);
        match.Info.MapId.Should().Be(11);
        match.Info.GameMode.Should().Be("CLASSIC");
        match.Info.GameType.Should().Be("MATCHED_GAME");
        match.Info.GameVersion.Should().Be("16.13.703.1234");
        match.Info.GameDuration.Should().Be(1800);
        match.Info.GameStartTimestamp.Should().Be(1751200060000);

        var participant = match.Info.Participants.Should().ContainSingle().Subject;
        participant.ParticipantId.Should().Be(1);
        participant.Puuid.Should().Be("puuid-a");
        participant.SummonerName.Should().Be("Phantasm");
        participant.SummonerLevel.Should().Be(412);
        participant.ChampionId.Should().Be(157);
        participant.TeamId.Should().Be(100);
        participant.TeamPosition.Should().Be("MIDDLE");
        participant.IndividualPosition.Should().Be("MIDDLE");
        participant.Lane.Should().Be("MIDDLE");
        participant.Role.Should().Be("SOLO");
        participant.Win.Should().BeTrue();
        participant.Kills.Should().Be(12);
        participant.Deaths.Should().Be(3);
        participant.Assists.Should().Be(7);
        participant.TotalDamageDealtToChampions.Should().Be(28450);
        participant.VisionScore.Should().Be(21);
        participant.GoldEarned.Should().Be(15320);
        participant.TotalMinionsKilled.Should().Be(245);
        participant.NeutralMinionsKilled.Should().Be(12);
        participant.ChampLevel.Should().Be(17);
        participant.Item0.Should().Be(6673);
        participant.Item6.Should().Be(3363);
        participant.Summoner1Id.Should().Be(4);
        participant.Summoner2Id.Should().Be(14);

        participant.Perks.StatPerks.Defense.Should().Be(5011);
        participant.Perks.StatPerks.Flex.Should().Be(5008);
        participant.Perks.StatPerks.Offense.Should().Be(5005);
        participant.Perks.Styles.Should().HaveCount(2);
        participant.Perks.Styles[0].Description.Should().Be("primaryStyle");
        participant.Perks.Styles[0].Style.Should().Be(8000);
        participant.Perks.Styles[0].Selections.Select(selection => selection.Perk)
            .Should().Equal(8008, 9111);
        participant.Perks.Styles[1].Description.Should().Be("subStyle");
        participant.Perks.Styles[1].Style.Should().Be(8100);
    }

    [Fact]
    public async Task GetTimelineAsync_DeserialisesAndMapsARealisticTimelinePayload()
    {
        using var handler = new StubHandler(TimelinePayload);
        using var httpClient = new HttpClient(handler);
        var client = new RiotMatchClient(httpClient);

        var timeline = await client.GetTimelineAsync("KR_7412001234", RegionalRoute.Asia, CancellationToken.None);

        var frame = timeline.Frames.Should().ContainSingle().Subject;
        frame.TimestampMs.Should().Be(300000);

        var participantFrame = frame.ParticipantFrames.Should().ContainSingle().Subject;
        participantFrame.ParticipantId.Should().Be(1);
        participantFrame.TotalGold.Should().Be(2450);
        participantFrame.CurrentGold.Should().Be(420);
        participantFrame.Level.Should().Be(6);
        participantFrame.Xp.Should().Be(3100);
        participantFrame.MinionsKilled.Should().Be(44);
        participantFrame.JungleMinionsKilled.Should().Be(2);
        participantFrame.TotalDamageToChampions.Should().Be(3120);
        participantFrame.X.Should().Be(7400);
        participantFrame.Y.Should().Be(7100);

        timeline.Events.Should().HaveCount(3);
        var kill = timeline.Events.Should().ContainSingle(e => e.Type == "CHAMPION_KILL").Subject;
        kill.KillerId.Should().Be(1);
        kill.VictimId.Should().Be(6);
        kill.AssistingParticipantIds.Should().Equal(2, 3);
        kill.PositionX.Should().Be(7500);
        kill.PositionY.Should().Be(7200);

        var purchase = timeline.Events.Should().ContainSingle(e => e.Type == "ITEM_PURCHASED").Subject;
        purchase.ItemId.Should().Be(1055);
        purchase.TimestampMs.Should().Be(121000);
    }

    [Fact]
    public async Task GetMatchIdsAsync_DeserialisesTheRawStringArray()
    {
        using var handler = new StubHandler("""["KR_1","KR_2","KR_3"]""");
        using var httpClient = new HttpClient(handler);
        var client = new RiotMatchClient(httpClient);

        var ids = await client.GetMatchIdsAsync(
            new MatchIdQuery("puuid-a", RegionalRoute.Asia, 3),
            CancellationToken.None);

        ids.Should().Equal("KR_1", "KR_2", "KR_3");
    }

    [Fact]
    public async Task PlatformClient_DeserialisesLeagueSummonerAndMasteryPayloads()
    {
        using var leagueHandler = new StubHandler("""
        {
          "tier": "CHALLENGER",
          "queue": "RANKED_SOLO_5x5",
          "entries": [
            { "summonerId": "sum-1", "puuid": "puuid-a", "rank": "I", "leaguePoints": 1204, "wins": 300, "losses": 250 }
          ]
        }
        """);
        using var leagueClient = new HttpClient(leagueHandler);
        var league = await new RiotPlatformClient(leagueClient)
            .GetChallengerLeagueAsync(PlatformRoute.KR, "RANKED_SOLO_5x5", CancellationToken.None);

        league.Tier.Should().Be("CHALLENGER");
        var entry = league.Entries.Should().ContainSingle().Subject;
        entry.SummonerId.Should().Be("sum-1");
        entry.Puuid.Should().Be("puuid-a");
        entry.Rank.Should().Be("I");
        entry.LeaguePoints.Should().Be(1204);
        entry.Wins.Should().Be(300);
        entry.Losses.Should().Be(250);

        using var summonerHandler = new StubHandler("""
        { "id": "sum-1", "accountId": "acc-1", "puuid": "puuid-a", "name": "Phantasm", "profileIconId": 4568, "revisionDate": 1751200000000, "summonerLevel": 412 }
        """);
        using var summonerClient = new HttpClient(summonerHandler);
        var summoner = await new RiotPlatformClient(summonerClient)
            .GetSummonerByPuuidAsync(PlatformRoute.KR, "puuid-a", CancellationToken.None);

        summoner.Id.Should().Be("sum-1");
        summoner.Puuid.Should().Be("puuid-a");
        summoner.Name.Should().Be("Phantasm");
        summoner.ProfileIconId.Should().Be(4568);
        summoner.SummonerLevel.Should().Be(412);

        using var masteryHandler = new StubHandler("""
        [
          { "puuid": "puuid-a", "championId": 157, "championLevel": 7, "championPoints": 412300, "lastPlayTime": 1751190000000 },
          { "puuid": "puuid-a", "championId": 238, "championLevel": 6, "championPoints": 98000, "lastPlayTime": 1751000000000 }
        ]
        """);
        using var masteryClient = new HttpClient(masteryHandler);
        var masteries = await new RiotPlatformClient(masteryClient)
            .GetChampionMasteriesAsync(PlatformRoute.KR, "puuid-a", CancellationToken.None);

        masteries.Should().HaveCount(2);
        masteries[0].ChampionId.Should().Be(157);
        masteries[0].ChampionPoints.Should().Be(412300);
        masteries[0].LastPlayTime.Should().Be(1751190000000);

        using var entriesHandler = new StubHandler("""
        [
          { "queueType": "RANKED_SOLO_5x5", "tier": "DIAMOND", "rank": "II", "leaguePoints": 62, "wins": 120, "losses": 100 },
          { "queueType": "RANKED_FLEX_SR", "tier": "PLATINUM", "rank": "I", "leaguePoints": 10, "wins": 20, "losses": 18 }
        ]
        """);
        using var entriesClient = new HttpClient(entriesHandler);
        var entries = await new RiotPlatformClient(entriesClient)
            .GetLeagueEntriesByPuuidAsync(PlatformRoute.KR, "puuid-a", CancellationToken.None);

        entries.Should().HaveCount(2);
        entries[0].QueueType.Should().Be("RANKED_SOLO_5x5");
        entries[0].Tier.Should().Be("DIAMOND");
        entries[0].Rank.Should().Be("II");
        entries[0].LeaguePoints.Should().Be(62);
    }

    [Fact]
    public void RiotJsonContext_ResolvesEveryRootTypeTheClientsRequest()
    {
        Type[] roots =
        [
            typeof(RiotAccountDto),
            typeof(RiotSummonerDto),
            typeof(RiotLeagueListDto),
            typeof(List<RiotLeagueEntryByPuuidDto>),
            typeof(List<RiotChampionMasteryDto>),
            typeof(RiotMatchDto),
            typeof(RiotTimelineDto),
            typeof(List<string>)
        ];

        foreach (var root in roots)
        {
            RiotJson.Options.TryGetTypeInfo(root, out _)
                .Should().BeTrue("{0} is a root type the Riot clients deserialize", root.Name);
        }
    }

    [Fact]
    public void RiotOptions_KeepTheWebCasingContract()
    {
        // Unchanged from the JsonSerializerOptions.Web the reflection path used:
        // camelCase names, case-insensitive matching, numbers readable from
        // strings. Every Riot DTO carries an explicit [JsonPropertyName] so
        // nothing depends on the case-insensitive fallback, but flipping it here
        // would be a silent behaviour change (#254).
        RiotJson.Options.PropertyNamingPolicy.Should().BeSameAs(System.Text.Json.JsonNamingPolicy.CamelCase);
        RiotJson.Options.PropertyNameCaseInsensitive
            .Should().Be(System.Text.Json.JsonSerializerOptions.Web.PropertyNameCaseInsensitive);
        RiotJson.Options.NumberHandling
            .Should().Be(System.Text.Json.JsonSerializerOptions.Web.NumberHandling);
    }

    private sealed class StubHandler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }
}
