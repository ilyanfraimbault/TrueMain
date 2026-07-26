using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Core.Lol.Map;
using Core.Lol.Ranking;
using Core.Options;
using Data.BuildFacts;
using Data.Entities;
using Ingestor.Options;
using Ingestor.Processes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.ReadModels.Champions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// End-to-end powerspikes read: seeds the dense per-minute snapshots + item events,
/// runs <see cref="ChampionPowerspikeAggregationProcess"/> to fold them into the
/// pre-aggregated stat tables, then asserts the endpoint reconstructs the event
/// spikes from those stats (#694) — the read no longer touches the raw grid. Spikes
/// are scoped to one core build (#890), so every request carries a build key.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChampionPowerspikesApiIntegrationTests
{
    private const int QueueId = 420;
    private const int Champion = 157; // Yone
    private const int Opponent = 238; // Zed
    private const string Position = "MIDDLE";
    private const string GameVersion = "16.4.521.123";

    // Aggregate scopes carry the normalized major.minor patch, not the raw Riot
    // build — mirror production so the patch-filtered read resolves the build.
    private const string ScopePatch = "16.4";

    private const int CoreItem = 3153;   // a completed (final) item — the build's first
    private const int NoiseItem = 1001;   // a component purchase that must be ignored

    // The core build every seeded game belongs to.
    private const int Keystone = 8112;
    private const int KeystoneCatalogId = 1;
    private static readonly string BuildQuery = $"buildFirstItemId={CoreItem}&buildKeystoneId={Keystone}";

    // The gold/damage lead is flat until each game's own kink minute, then rises —
    // a deliberate upward slope kink. The level-6 minute and the core item completion
    // are both placed at that kink. The kink minute is spread across games (centred
    // here) so the mean curve smears into a gentle ramp: each game keeps a sharp local
    // spike while the population baseline stays shallow, so the baseline-subtracted
    // spike (#775) comes out clearly positive — a single shared kink would cancel to
    // ~zero against its own baseline.
    private const int KinkMinute = 12;
    private const int KinkSpread = 2; // kink minutes span [KinkMinute - 2, KinkMinute + 2]
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

        var response = await client.GetAsync($"/champions/{Champion}/powerspikes?position={Position}&{BuildQuery}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var spikes = await response.Content.ReadFromJsonAsync<ChampionPowerspikesResponse>();
        spikes.Should().NotBeNull();
        spikes!.ChampionId.Should().Be(Champion);
        spikes.Position.Should().Be(Position);

        // The completed item is detected and shows a positive spike at the kink.
        var itemSpike = spikes.Events.SingleOrDefault(e => e.Type == "item" && e.RefId == CoreItem);
        itemSpike.Should().NotBeNull("the build's completed item is the item event");
        itemSpike!.SpikeMagnitude.Should().BePositive("the power curve accelerates right after the item");
        itemSpike.AvgMinute.Should().BeApproximately(KinkMinute, 0.5);
        itemSpike.Games.Should().Be(12);

        // The component purchase is not a completed item, so it is never a spike —
        // the completion test is the item metadata, not the final inventory (#890).
        spikes.Events.Should().NotContain(e => e.Type == "item" && e.RefId == NoiseItem);

        // Level 6 is reached at the kink and also spikes positively.
        var level6 = spikes.Events.SingleOrDefault(e => e.Type == "level" && e.RefId == 6);
        level6.Should().NotBeNull();
        level6!.SpikeMagnitude.Should().BePositive();
        level6.AvgMinute.Should().BeApproximately(KinkMinute, 0.5);
    }

    [Fact]
    public async Task GetChampionPowerspikesAsync_PatchFilterStillReturnsItemSpikes()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync(games: 12);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // Event rows carry the normalized patch ("16.4") while matches carry the raw
        // Riot build ("16.4.521.123"); the patch-scoped read must match both sides or
        // every spike silently vanishes.
        var spikes = await client.GetFromJsonAsync<ChampionPowerspikesResponse>(
            $"/champions/{Champion}/powerspikes?position={Position}&patch={ScopePatch}&{BuildQuery}");

        spikes!.Patch.Should().Be(ScopePatch);
        spikes.Events.Should().Contain(e => e.Type == "item" && e.RefId == CoreItem);
    }

    [Fact]
    public async Task GetChampionPowerspikesAsync_ReturnsNoEventsForAnotherCoreBuild()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync(games: 12);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // A build key nobody played must come back empty rather than falling back to
        // the champion's other builds — that blending is exactly what #890 removes.
        var spikes = await client.GetFromJsonAsync<ChampionPowerspikesResponse>(
            $"/champions/{Champion}/powerspikes?position={Position}&buildFirstItemId={CoreItem}&buildKeystoneId=9999");

        spikes!.Events.Should().BeEmpty();
    }

    [Theory]
    // Neither half, each half alone, and a non-positive value: the build key is
    // only meaningful as a complete, positive pair.
    // Literals rather than the CoreItem/Keystone constants: attribute arguments
    // must be compile-time constants, and interpolation is not.
    [InlineData("")]
    [InlineData("&buildFirstItemId=3153")]
    [InlineData("&buildKeystoneId=8112")]
    [InlineData("&buildFirstItemId=0&buildKeystoneId=8112")]
    [InlineData("&buildFirstItemId=3153&buildKeystoneId=-1")]
    public async Task GetChampionPowerspikesAsync_ReturnsBadRequestForIncompleteBuildKey(string buildQuery)
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/powerspikes?position={Position}{buildQuery}");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetChampionPowerspikesAsync_FiltersToRequestedEloBracket()
    {
        await _fixture.ResetDatabaseAsync();
        // Two cohorts with the same power curve: 12 Gold games and 12 Iron games.
        await SeedBracketedAsync(perBracketGames: 12);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // ALL sees both cohorts on every event.
        var all = await client.GetFromJsonAsync<ChampionPowerspikesResponse>(
            $"/champions/{Champion}/powerspikes?position={Position}&{BuildQuery}");
        all!.Events.Should().NotBeEmpty();
        // The item completion happens in every seeded game, so its game count is the
        // whole cohort — level milestones are reached in varying numbers of games.
        all.Events.Single(e => e.Type == "item" && e.RefId == CoreItem).Games.Should().Be(24);

        // A bare Gold filter narrows the champion side to the Gold-stamped games.
        var gold = await client.GetFromJsonAsync<ChampionPowerspikesResponse>(
            $"/champions/{Champion}/powerspikes?position={Position}&eloBracket=GOLD&{BuildQuery}");
        gold!.Events.Should().NotBeEmpty();
        gold.Events.Single(e => e.Type == "item" && e.RefId == CoreItem).Games.Should().Be(12);

        // GOLD_PLUS unions Gold and above; Iron is below and drops out.
        var goldPlus = await client.GetFromJsonAsync<ChampionPowerspikesResponse>(
            $"/champions/{Champion}/powerspikes?position={Position}&eloBracket=GOLD_PLUS&{BuildQuery}");
        goldPlus!.Events.Single(e => e.Type == "item" && e.RefId == CoreItem).Games.Should().Be(12);
    }

    [Fact]
    public async Task GetChampionPowerspikesAsync_ReturnsBadRequestForInvalidPosition()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/powerspikes?position=NOTALANE&{BuildQuery}");
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
        AddKeystoneCatalog(db);

        for (var i = 0; i < games; i++)
        {
            AddSpikeGame(db, $"m-spike-{i}", i, games, account.Id, eloBracket: "");
        }

        await db.SaveChangesAsync();

        var aggregatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        await new ChampionAggregateSeeder()
            .AddPatternDefaults(
                account.Id, Champion, ScopePatch, platformId: "EUW1", QueueId, Position,
                summoner1Id: 4, summoner2Id: 14, skillOrderKey: "Q",
                buildItems: [CoreItem], bootsItemId: 0, games: games, wins: games / 2, aggregatedAt)
            .SaveAsync(db);

        await RunAggregationAsync();
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
        AddKeystoneCatalog(db);

        for (var i = 0; i < perBracketGames; i++)
        {
            AddSpikeGame(db, $"m-spike-gold-{i}", i, perBracketGames, account.Id, EloBracket.Gold);
        }
        for (var i = 0; i < perBracketGames; i++)
        {
            AddSpikeGame(db, $"m-spike-iron-{i}", i, perBracketGames, account.Id, EloBracket.Iron);
        }

        await db.SaveChangesAsync();

        await RunAggregationAsync();
    }

    // Fold the seeded dense snapshots into the powerspike stat tables the read now
    // consumes, exactly as the ingestor's incremental aggregation does in production.
    private async Task RunAggregationAsync()
    {
        var process = new ChampionPowerspikeAggregationProcess(
            NullLogger<ChampionPowerspikeAggregationProcess>.Instance,
            Microsoft.Extensions.Options.Options.Create(new PowerspikeAggregationOptions()),
            Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions { QueueId = LolQueueId.RankedSoloDuo }),
            new TestDbContextFactory(_fixture),
            new FakeItemMetadataProvider(),
            TimeProvider.System);

        await process.RunCoreAsync(CancellationToken.None);
    }

    private static void AddSpikeGame(
        Data.TrueMainDbContext db, string matchId, int index, int cohortGames, Guid accountId, string eloBracket)
    {
        db.Matches.Add(new MatchBuilder()
            .WithId(matchId)
            .WithQueueId(QueueId)
            .WithGameVersion(GameVersion)
            .WithTimelineIngested()
            .Build());

        // Each game's kink sits at a different minute in [KinkMinute ± KinkSpread], so
        // the cohort's kinks average out to ~KinkMinute while no single minute carries
        // the whole slope change — the population baseline stays shallow.
        var kink = KinkMinute + (index % (2 * KinkSpread + 1)) - KinkSpread;

        var champion = Participant(matchId, 1, Champion, teamId: 100, win: true, accountId, eloBracket);
        // The aggregation detects completions from the purchase events + item
        // metadata, but the build key still comes from the final inventory, so the
        // completed item must also sit in a build slot.
        champion.Item0 = CoreItem;
        champion.ItemEvents =
        [
            new ItemEvent { EventType = "ITEM_PURCHASED", ItemId = NoiseItem, TimestampMs = 5 * 60_000 },
            new ItemEvent { EventType = "ITEM_PURCHASED", ItemId = CoreItem, TimestampMs = kink * 60_000 }
        ];
        db.MatchParticipants.Add(champion);
        db.MatchParticipants.Add(Participant(matchId, 2, Opponent, teamId: 200, win: false));

        // The keystone half of the build key.
        db.ParticipantPerkSelections.Add(new ParticipantPerkSelection
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            ParticipantId = 1,
            PerkSelectionCatalogId = KeystoneCatalogId
        });

        // Per-game offset so the lead varies across the cohort — the spread (sigma)
        // is then non-zero and power is normalizable even under a bracket filter.
        var variance = (index - cohortGames / 2) * 4;

        for (var minute = 1; minute <= MaxMinute; minute++)
        {
            var goldDiff = GoldDiffBase(minute, kink) + variance;
            var dmgDiff = DamageDiffBase(minute, kink) + variance;
            var level = minute < kink ? 5 : Math.Min(18, 6 + (minute - kink) / 3);

            var championGold = minute * 300;
            var championDamage = minute * 150;

            db.MatchParticipantTimelineSnapshots.Add(
                Snapshot(matchId, 1, minute, championGold, level, championDamage));
            db.MatchParticipantTimelineSnapshots.Add(
                Snapshot(matchId, 2, minute, championGold - goldDiff, level - 1, championDamage - dmgDiff));
        }
    }

    // Flat lead up to the game's kink minute, then a linear rise — an upward slope kink.
    private static int GoldDiffBase(int minute, int kink)
        => minute <= kink ? 100 : 100 + (minute - kink) * 80;

    private static int DamageDiffBase(int minute, int kink)
        => minute <= kink ? 50 : 50 + (minute - kink) * 40;

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

    private static void AddKeystoneCatalog(Data.TrueMainDbContext db)
        => db.PerkSelectionCatalogs.Add(new PerkSelectionCatalog
        {
            Id = KeystoneCatalogId,
            StyleId = 8100,
            SelectionIndex = 0,
            PerkId = Keystone,
            StyleDescription = "primaryStyle"
        });

    private sealed class FakeItemMetadataProvider : IItemMetadataProvider
    {
        // CoreItem completes; NoiseItem is a component that never does — the spike
        // detection must key off IsFinalItem, not off the final inventory.
        private static readonly IReadOnlyDictionary<int, ItemMetadata> Metadata =
            new Dictionary<int, ItemMetadata>
            {
                [CoreItem] = new(CoreItem, 3200, true, false, false, false, true, false),
                [NoiseItem] = new(NoiseItem, 400, true, false, false, false, false, false)
            };

        public Task<IReadOnlyDictionary<int, ItemMetadata>> GetItemsAsync(string gameVersion, CancellationToken ct)
            => Task.FromResult(Metadata);
    }
}
