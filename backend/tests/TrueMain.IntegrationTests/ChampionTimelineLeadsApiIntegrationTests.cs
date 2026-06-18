using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Champions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ChampionTimelineLeadsApiIntegrationTests
{
    private const int QueueId = 420;
    private const int Champion = 157; // Yone
    private const int Opponent = 238; // Zed
    private const string Position = "MIDDLE";
    private static readonly int[] Intervals = [5, 10, 15, 20, 30];

    // Each opponent snapshot is the champion's value minus this fixed delta, so
    // every per-interval average lead is exactly the delta regardless of game count.
    private const int GoldLead = 500;
    private const int CsLead = 5;
    private const int KillsLead = 1;
    private const int LevelLead = 1;
    private const int XpLead = 200;
    private const int DamageLead = 300;

    private readonly PostgresFixture _fixture;

    public ChampionTimelineLeadsApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetChampionTimelineLeadsAsync_AveragesLeadVsLaneOpponentPerInterval()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedLeadSampleAsync(games: 12);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/timeline-leads?position={Position}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var leads = await response.Content.ReadFromJsonAsync<ChampionTimelineLeadsResponse>();
        leads.Should().NotBeNull();
        leads!.ChampionId.Should().Be(Champion);
        leads.Position.Should().Be(Position);
        leads.Intervals.Select(i => i.IntervalMinute).Should().Equal(Intervals);

        foreach (var interval in leads.Intervals)
        {
            interval.Games.Should().Be(12);
            interval.GoldDiff.Should().BeApproximately(GoldLead, 1e-9);
            interval.CsDiff.Should().BeApproximately(CsLead, 1e-9);
            interval.KillsDiff.Should().BeApproximately(KillsLead, 1e-9);
            interval.LevelDiff.Should().BeApproximately(LevelLead, 1e-9);
            interval.XpDiff.Should().BeApproximately(XpLead, 1e-9);
            interval.DamageDiff.Should().BeApproximately(DamageLead, 1e-9);
        }
    }

    [Fact]
    public async Task GetChampionTimelineLeadsAsync_DropsIntervalsBelowSampleFloor()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedLeadSampleAsync(games: 5); // below MinMatchupGames floor of 10

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/timeline-leads?position={Position}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var leads = await response.Content.ReadFromJsonAsync<ChampionTimelineLeadsResponse>();
        leads!.Intervals.Should().BeEmpty("five games is below the sample floor");
    }

    [Fact]
    public async Task GetChampionTimelineLeadsAsync_ReturnsBadRequestForInvalidPosition()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/timeline-leads?position=NOTALANE");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task SeedLeadSampleAsync(int games)
    {
        await using var db = _fixture.CreateDbContext();

        var account = new RiotAccountBuilder()
            .WithGameName("LeadsMain")
            .WithTagLine("KR1")
            .WithPuuid("leads-main-puuid")
            .Build();
        db.RiotAccounts.Add(account);

        for (var i = 0; i < games; i++)
        {
            var matchId = $"m-leads-{i}";
            var match = new MatchBuilder()
                .WithId(matchId)
                .WithQueueId(QueueId)
                .WithGameVersion("16.4.521.123")
                .Build();
            db.Matches.Add(match);

            db.MatchParticipants.Add(Participant(matchId, 1, Champion, teamId: 100, win: true, riotAccountId: account.Id));
            db.MatchParticipants.Add(Participant(matchId, 2, Opponent, teamId: 200, win: false));

            foreach (var minute in Intervals)
            {
                // Champion-side values are arbitrary; the opponent is offset by a
                // fixed lead so the averaged diff is exactly that lead.
                var gold = minute * 200;
                var cs = minute * 5;
                var level = minute / 3 + 4;
                var xp = minute * 250;
                var kills = minute / 5;
                var damage = minute * 200;

                db.MatchParticipantTimelineSnapshots.Add(
                    Snapshot(matchId, 1, minute, gold, cs, level, xp, kills, damage));
                db.MatchParticipantTimelineSnapshots.Add(
                    Snapshot(matchId, 2, minute,
                        gold - GoldLead, cs - CsLead, level - LevelLead, xp - XpLead, kills - KillsLead, damage - DamageLead));
            }
        }

        await db.SaveChangesAsync();
    }

    private static MatchParticipantTimelineSnapshot Snapshot(
        string matchId, int participantId, int minute,
        int gold, int cs, int level, int xp, int kills, int damage)
        => new()
        {
            MatchId = matchId,
            ParticipantId = participantId,
            IntervalMinute = minute,
            TimestampMs = minute * 60_000,
            TotalGold = gold,
            MinionsKilled = cs,
            JungleMinionsKilled = 0,
            Level = level,
            Xp = xp,
            Kills = kills,
            DamageToChampions = damage,
            WardsPlaced = 0,
            WardsKilled = 0
        };

    private static MatchParticipant Participant(
        string matchId, int participantId, int championId, int teamId, bool win, Guid? riotAccountId = null)
        => new()
        {
            MatchId = matchId,
            ParticipantId = participantId,
            Puuid = $"puuid-{matchId}-{participantId}",
            RiotAccountId = riotAccountId,
            SummonerName = "seed",
            SummonerLevel = 100,
            ChampionId = championId,
            TeamId = teamId,
            TeamPosition = Position,
            IndividualPosition = Position,
            Lane = Position,
            Role = "SOLO",
            Win = win,
            ChampLevel = 16,
            Item6 = 3363,
            TrinketItemId = 3363,
            ItemEvents = [],
            SkillEvents = []
        };

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [
                new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420"),
                new KeyValuePair<string, string?>("ChampionsList:MinMatchupGames", "10"),
                new KeyValuePair<string, string?>("ChampionsList:MinPlayerMatchupGames", "3"),
            ]);
}
