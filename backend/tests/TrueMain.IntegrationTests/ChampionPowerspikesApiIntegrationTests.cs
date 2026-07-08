using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Core.Lol.Ranking;
using Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Champions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ChampionPowerspikesApiIntegrationTests
{
    private const int QueueId = 420;
    private const int Champion = 157; // Yone
    private const int Opponent = 238; // Zed
    private const string Position = "MIDDLE";
    private const string GameVersion = "16.4.521.123";

    private const int CoreItem = 3153;   // completed item in the dominant build
    private const int NoiseItem = 1001;   // a non-build purchase that must be ignored

    // The gold/damage lead is flat up to this minute, then rises — a deliberate
    // upward kink. The first level-6 minute and the core item completion are both
    // placed here, so both events must show a positive spike (the slope of the
    // power curve increases right after them).
    private const int KinkMinute = 12;
    private const int MaxMinute = 30;

    private readonly PostgresFixture _fixture;

    public ChampionPowerspikesApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetChampionPowerspikesAsync_ReturnsCurveAndPositiveSpikesAtKink()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync(games: 12);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/powerspikes?position={Position}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var spikes = await response.Content.ReadFromJsonAsync<ChampionPowerspikesResponse>();
        spikes.Should().NotBeNull();
        spikes!.ChampionId.Should().Be(Champion);
        spikes.Position.Should().Be(Position);

        // The curve is populated (power is computable: the per-game variance gives
        // a non-zero global spread, so normalization does not divide by zero).
        spikes.Curve.Should().NotBeEmpty();
        spikes.Curve.Should().OnlyContain(point => point.Games == 12);

        // The core build item is detected and shows a positive spike at the kink.
        var itemSpike = spikes.Events.SingleOrDefault(e => e.Type == "item" && e.RefId == CoreItem);
        itemSpike.Should().NotBeNull("the dominant build's completed item is the item event");
        itemSpike!.SpikeMagnitude.Should().BePositive("the power curve accelerates right after the item");
        itemSpike.AvgMinute.Should().BeApproximately(KinkMinute, 0.5);
        itemSpike.Games.Should().Be(12);

        // The noise purchase (not in the build) must not appear.
        spikes.Events.Should().NotContain(e => e.Type == "item" && e.RefId == NoiseItem);

        // Level 6 is reached at the kink and also spikes positively.
        var level6 = spikes.Events.SingleOrDefault(e => e.Type == "level" && e.RefId == 6);
        level6.Should().NotBeNull();
        level6!.SpikeMagnitude.Should().BePositive();
        level6.AvgMinute.Should().BeApproximately(KinkMinute, 0.5);
    }

    [Fact]
    public async Task GetChampionPowerspikesAsync_FiltersToRequestedEloBracket()
    {
        await _fixture.ResetDatabaseAsync();
        // Two cohorts with the same power curve: 12 Gold games and 12 Iron games.
        await SeedBracketedAsync(perBracketGames: 12);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // ALL sees both cohorts on every curve point.
        var all = await client.GetFromJsonAsync<ChampionPowerspikesResponse>(
            $"/champions/{Champion}/powerspikes?position={Position}");
        all!.Curve.Should().NotBeEmpty();
        all.Curve.Should().OnlyContain(point => point.Games == 24);

        // A bare Gold filter narrows the champion side to the Gold-stamped games.
        var gold = await client.GetFromJsonAsync<ChampionPowerspikesResponse>(
            $"/champions/{Champion}/powerspikes?position={Position}&eloBracket=GOLD");
        gold!.Curve.Should().NotBeEmpty();
        gold.Curve.Should().OnlyContain(point => point.Games == 12);

        // GOLD_PLUS unions Gold and above; Iron is below and drops out.
        var goldPlus = await client.GetFromJsonAsync<ChampionPowerspikesResponse>(
            $"/champions/{Champion}/powerspikes?position={Position}&eloBracket=GOLD_PLUS");
        goldPlus!.Curve.Should().OnlyContain(point => point.Games == 12);
    }

    [Fact]
    public async Task GetChampionPowerspikesAsync_ReturnsBadRequestForInvalidPosition()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/powerspikes?position=NOTALANE");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task SeedAsync(int games)
    {
        await using var db = _fixture.CreateDbContext();

        var account = new RiotAccountBuilder()
            .WithGameName("SpikeMain")
            .WithTagLine("KR1")
            .WithPuuid("spike-main-puuid")
            .Build();
        db.RiotAccounts.Add(account);

        for (var i = 0; i < games; i++)
        {
            AddSpikeGame(db, $"m-spike-{i}", i, games, account.Id, eloBracket: "");
        }

        await db.SaveChangesAsync();

        var aggregatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        await new ChampionAggregateSeeder()
            .AddPatternDefaults(
                account.Id, Champion, GameVersion, platformId: "EUW1", QueueId, Position,
                summoner1Id: 4, summoner2Id: 14, skillOrderKey: "Q",
                buildItems: [CoreItem], bootsItemId: 0, games: games, wins: games / 2, aggregatedAt)
            .SaveAsync(db);
    }

    /// <summary>
    /// Seeds two power-curve cohorts for one tracked account — <paramref name="perBracketGames"/>
    /// Gold games and the same number of Iron games, each with an identical curve —
    /// so the elo-bracket filter narrows the champion side. Each cohort carries its
    /// own per-game variance, so its filtered spread stays normalizable.
    /// </summary>
    private async Task SeedBracketedAsync(int perBracketGames)
    {
        await using var db = _fixture.CreateDbContext();

        var account = new RiotAccountBuilder()
            .WithGameName("SpikeBracket")
            .WithTagLine("KR1")
            .WithPuuid("spike-bracket-puuid")
            .Build();
        db.RiotAccounts.Add(account);

        for (var i = 0; i < perBracketGames; i++)
        {
            AddSpikeGame(db, $"m-spike-gold-{i}", i, perBracketGames, account.Id, EloBracket.Gold);
        }
        for (var i = 0; i < perBracketGames; i++)
        {
            AddSpikeGame(db, $"m-spike-iron-{i}", i, perBracketGames, account.Id, EloBracket.Iron);
        }

        await db.SaveChangesAsync();
    }

    private static void AddSpikeGame(
        Data.TrueMainDbContext db, string matchId, int index, int cohortGames, Guid accountId, string eloBracket)
    {
        db.Matches.Add(new MatchBuilder()
            .WithId(matchId)
            .WithQueueId(QueueId)
            .WithGameVersion(GameVersion)
            .Build());

        var champion = Participant(matchId, 1, Champion, teamId: 100, win: true, accountId, eloBracket);
        champion.ItemEvents =
        [
            new ItemEvent { EventType = "ITEM_PURCHASED", ItemId = NoiseItem, TimestampMs = 5 * 60_000 },
            new ItemEvent { EventType = "ITEM_PURCHASED", ItemId = CoreItem, TimestampMs = KinkMinute * 60_000 }
        ];
        db.MatchParticipants.Add(champion);
        db.MatchParticipants.Add(Participant(matchId, 2, Opponent, teamId: 200, win: false));

        // Per-game offset so the lead varies across the cohort — the spread (sigma)
        // is then non-zero and power is normalizable even under a bracket filter.
        var variance = (index - cohortGames / 2) * 4;

        for (var minute = 1; minute <= MaxMinute; minute++)
        {
            var goldDiff = GoldDiffBase(minute) + variance;
            var dmgDiff = DamageDiffBase(minute) + variance;
            var level = minute < KinkMinute ? 5 : Math.Min(18, 6 + (minute - KinkMinute) / 3);

            var championGold = minute * 300;
            var championDamage = minute * 150;

            db.MatchParticipantTimelineSnapshots.Add(
                Snapshot(matchId, 1, minute, championGold, level, championDamage));
            db.MatchParticipantTimelineSnapshots.Add(
                Snapshot(matchId, 2, minute, championGold - goldDiff, level - 1, championDamage - dmgDiff));
        }
    }

    // Flat lead up to the kink minute, then a linear rise — an upward slope kink.
    private static int GoldDiffBase(int minute)
        => minute <= KinkMinute ? 100 : 100 + (minute - KinkMinute) * 80;

    private static int DamageDiffBase(int minute)
        => minute <= KinkMinute ? 50 : 50 + (minute - KinkMinute) * 40;

    private static MatchParticipantTimelineSnapshot Snapshot(
        string matchId, int participantId, int minute, int gold, int level, int damage)
        => new()
        {
            MatchId = matchId,
            ParticipantId = participantId,
            IntervalMinute = minute,
            TimestampMs = minute * 60_000,
            TotalGold = gold,
            MinionsKilled = minute * 5,
            JungleMinionsKilled = 0,
            Level = level,
            Xp = minute * 250,
            Kills = minute / 5,
            DamageToChampions = damage,
            WardsPlaced = 0,
            WardsKilled = 0
        };

    private static MatchParticipant Participant(
        string matchId, int participantId, int championId, int teamId, bool win,
        Guid? riotAccountId = null, string eloBracket = "")
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
            EloBracket = eloBracket,
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
