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
using Microsoft.EntityFrameworkCore;
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
    private const int SecondOpponent = 103; // Ahri — the other side of the matchup split (#957)
    private const string Position = "MIDDLE";
    private const string GameVersion = "16.4.521.123";

    // Aggregate scopes carry the normalized major.minor patch, not the raw Riot
    // build — mirror production so the patch-filtered read resolves the build.
    private const string ScopePatch = "16.4";

    private const int CoreItem = 3153;   // a completed (final) item — the build's first
    private const int NoiseItem = 1001;   // a component purchase that must be ignored

    // A second completed legendary the games buy but the build's core path does not
    // contain. It is eligible, it folds, and it produces an event row — the read has
    // to drop it on the path intersection alone (#1021).
    private const int OffPathItem = 3157;

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
    public async Task GetChampionPowerspikesAsync_ReturnsOnlyTheCoreBuildPathItemsInBuildOrder()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync(games: 12);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var spikes = await client.GetFromJsonAsync<ChampionPowerspikesResponse>(
            $"/champions/{Champion}/powerspikes?position={Position}&{BuildQuery}");

        // Both items were completed in all 12 games and both folded into event rows —
        // asserted here so the test cannot pass because the fold dropped one of them.
        await using (var db = _fixture.CreateDbContext())
        {
            var folded = db.ChampionPowerspikeEventStats
                .Where(e => e.EventType == "item")
                .Select(e => e.RefId)
                .ToList();
            folded.Should().Contain([CoreItem, OffPathItem]);
        }

        var itemRefIds = spikes!.Events.Where(e => e.Type == "item").Select(e => e.RefId).ToList();
        itemRefIds.Should().Equal([CoreItem],
            "only the items of this build's core path are its power spikes");

        // Items come before the level milestones, and the level milestones keep their
        // own ascending order — the payload is a display order, not a ranking.
        var types = spikes.Events.Select(e => e.Type).ToList();
        types.Should().BeEquivalentTo(types.OrderBy(t => t == "item" ? 0 : 1), o => o.WithStrictOrdering());
        spikes.Events.Where(e => e.Type == "level").Select(e => e.RefId)
            .Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetChampionPowerspikesAsync_OrdersItemsByTheirSlotInAMultiItemCoreBuildPath()
    {
        await _fixture.ResetDatabaseAsync();
        // Same games, but the aggregate now says this build takes both items — so
        // both are core, and the order must be the build's. The seeded games complete
        // the second path item *earlier* than the first (OffPathItem at kink−4,
        // CoreItem at kink), so mean-minute order is the exact reverse of build
        // order: an accidental sort by minute — the one the client used to impose —
        // fails this assertion rather than passing by coincidence.
        await SeedAsync(games: 12, corePath: [CoreItem, OffPathItem]);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var spikes = await client.GetFromJsonAsync<ChampionPowerspikesResponse>(
            $"/champions/{Champion}/powerspikes?position={Position}&{BuildQuery}");

        var items = spikes!.Events.Where(e => e.Type == "item").ToList();
        items.Select(e => e.RefId).Should().Equal([CoreItem, OffPathItem]);
        items[0]!.AvgMinute.Should().BeGreaterThan(items[1]!.AvgMinute,
            "the first path item completes later than the second, so build order and "
            + "minute order genuinely disagree here");
    }

    [Fact]
    public async Task GetChampionPowerspikesAsync_WithholdsItemSpikesWhenNoCoreBuildPathResolves()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync(games: 12);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // Drop the pattern aggregate while leaving the folded event rows in place —
        // the state retention can genuinely leave behind, since the two sides age out
        // under different rules. Item spikes are then withheld rather than answered
        // with every item the slice happened to complete: showing items that are not
        // the build's is the failure #1021 is about. Level milestones are unaffected;
        // they are not build items and no path applies to them.
        await using (var db = _fixture.CreateDbContext())
        {
            await db.ChampionAggregatePatterns.ExecuteDeleteAsync();
            await db.ChampionAggregateScopes.ExecuteDeleteAsync();
        }

        var spikes = await client.GetFromJsonAsync<ChampionPowerspikesResponse>(
            $"/champions/{Champion}/powerspikes?position={Position}&{BuildQuery}");

        spikes!.Events.Should().NotContain(e => e.Type == "item");
        spikes.Events.Should().Contain(e => e.Type == "level");
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
    public async Task GetChampionPowerspikesAsync_NarrowsToTheRequestedLaneOpponent()
    {
        await _fixture.ResetDatabaseAsync();
        // 6 games against Zed and 6 against the second opponent, identical curves.
        await SeedTwoOpponentsAsync(perOpponentGames: 6);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // Unscoped, the read sums across opponents — the split is a refinement of the
        // grain, so it must not move the number the page showed before #957.
        var all = await client.GetFromJsonAsync<ChampionPowerspikesResponse>(
            $"/champions/{Champion}/powerspikes?position={Position}&{BuildQuery}");
        all!.Events.Single(e => e.Type == "item" && e.RefId == CoreItem).Games.Should().Be(12);

        // Scoped, only that opponent's games. 6 sits under MinMatchupGames (10): the
        // floor is deliberately not applied to a matchup slice, so this also pins the
        // bypass — without it the section would be empty for nearly every matchup.
        var versusZed = await client.GetFromJsonAsync<ChampionPowerspikesResponse>(
            $"/champions/{Champion}/powerspikes?position={Position}&{BuildQuery}&opponentChampionId={Opponent}");
        versusZed!.Events.Single(e => e.Type == "item" && e.RefId == CoreItem).Games.Should().Be(6);

        var versusOther = await client.GetFromJsonAsync<ChampionPowerspikesResponse>(
            $"/champions/{Champion}/powerspikes?position={Position}&{BuildQuery}&opponentChampionId={SecondOpponent}");
        versusOther!.Events.Single(e => e.Type == "item" && e.RefId == CoreItem).Games.Should().Be(6);
    }

    [Fact]
    public async Task GetChampionPowerspikesAsync_ReturnsNoEventsForAnUnplayedMatchup()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync(games: 12);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // A matchup with no folded game is empty, not a fallback onto the champion's
        // global spikes — the same rule the build key follows (#890). It is the state
        // every matchup is in until enough matches have been folded since #957, so it
        // has to be a clean empty rather than a silently wrong answer.
        var response = await client.GetAsync(
            $"/champions/{Champion}/powerspikes?position={Position}&{BuildQuery}&opponentChampionId=99999");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var spikes = await response.Content.ReadFromJsonAsync<ChampionPowerspikesResponse>();
        spikes!.Events.Should().BeEmpty();
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

    /// <param name="corePath">
    /// The item path the pattern aggregate reports for this build, i.e. what the
    /// build tab shows as its core. Defaults to the single first item; the read
    /// intersects the folded item events with it.
    /// </param>
    private async Task SeedAsync(int games, IReadOnlyList<int>? corePath = null)
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

        await SeedBuildAggregateAsync(db, account.Id, games, corePath ?? [CoreItem], EloBracket.Gold);

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

        // One pattern slice per bracket, so the core build path resolves on the same
        // bands the events are read on — a Gold-only request must not fall through to
        // a path derived from the Iron cohort.
        await SeedBuildAggregateAsync(
            db, account.Id, perBracketGames, [CoreItem], EloBracket.Gold, EloBracket.Iron);

        await RunAggregationAsync();
    }

    /// <summary>
    /// Seeds the same champion, position and core build against two different lane
    /// opponents, <paramref name="perOpponentGames"/> games each, so the matchup
    /// filter (#957) has something to separate and the unscoped read has something
    /// to sum back together.
    /// </summary>
    private async Task SeedTwoOpponentsAsync(int perOpponentGames)
    {
        await using var db = _fixture.CreateDbContext();

        var account = new RiotAccountBuilder()
            .WithGameName("SpikeMatchup")
            .WithTagLine("KR1")
            .WithPuuid("spike-matchup-puuid")
            .Build();
        db.RiotAccounts.Add(account);
        AddKeystoneCatalog(db);

        for (var i = 0; i < perOpponentGames; i++)
        {
            AddSpikeGame(db, $"m-spike-zed-{i}", i, perOpponentGames, account.Id, eloBracket: "", Opponent);
        }
        for (var i = 0; i < perOpponentGames; i++)
        {
            AddSpikeGame(db, $"m-spike-ahri-{i}", i, perOpponentGames, account.Id, eloBracket: "", SecondOpponent);
        }

        await db.SaveChangesAsync();

        // The opponent is a dimension of the event rows, not of the pattern
        // aggregate, so one slice covers both matchups' core path.
        await SeedBuildAggregateAsync(db, account.Id, 2 * perOpponentGames, [CoreItem], EloBracket.Gold);

        await RunAggregationAsync();
    }

    /// <summary>
    /// Seeds the pattern aggregate for the seeded core build. The powerspike read
    /// derives which items belong to this build — and in which order — from these
    /// rows (#1021): the event table alone cannot tell the build's items from the
    /// situational ones, so without a pattern slice carrying the same
    /// <c>(BuildItem0, PrimaryKeystoneId)</c> pair the request carries, item spikes
    /// are withheld rather than guessed. Production always has both, because the
    /// same fold writes them.
    /// </summary>
    /// <remarks>
    /// One seeder instance for every bracket, not one per call: the dim tables are
    /// globally deduplicated (the same build shape is a single row whatever scope
    /// references it), and that dedup cache lives on the seeder — two instances each
    /// insert the row and collide on its unique index.
    /// </remarks>
    private static Task SeedBuildAggregateAsync(
        Data.TrueMainDbContext db, Guid accountId, int games,
        IReadOnlyList<int> buildItems, params string[] eloBrackets)
    {
        var seeder = new ChampionAggregateSeeder();

        foreach (var eloBracket in eloBrackets)
        {
            seeder.AddPatternWithRune(
                accountId, Champion, ScopePatch, platformId: "EUW1", QueueId, Position,
                summoner1Id: 4, summoner2Id: 14, skillOrderKey: "Q",
                buildItems, bootsItemId: 0,
                primaryStyleId: 8100, primaryKeystoneId: Keystone, secondaryStyleId: 8000,
                games: games, wins: games / 2,
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), eloBracket);
        }

        return seeder.SaveAsync(db);
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
        Data.TrueMainDbContext db, string matchId, int index, int cohortGames, Guid accountId, string eloBracket,
        int opponentChampionId = Opponent)
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
            new ItemEvent { EventType = "ITEM_PURCHASED", ItemId = CoreItem, TimestampMs = kink * 60_000 },
            // Completed in every game and, by default, absent from the build's core
            // path: the fold records it, the read must not show it (#1021). Bought
            // *before* the core item on purpose — the one test that does put it on
            // the path relies on build order and minute order disagreeing.
            new ItemEvent { EventType = "ITEM_PURCHASED", ItemId = OffPathItem, TimestampMs = (kink - 4) * 60_000 }
        ];
        db.MatchParticipants.Add(champion);
        db.MatchParticipants.Add(Participant(matchId, 2, opponentChampionId, teamId: 200, win: false));

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
                [NoiseItem] = new(NoiseItem, 400, true, false, false, false, false, false),
                [OffPathItem] = new(OffPathItem, 2900, true, false, false, false, true, false)
            };

        public Task<IReadOnlyDictionary<int, ItemMetadata>> GetItemsAsync(string gameVersion, CancellationToken ct)
            => Task.FromResult(Metadata);
    }
}
