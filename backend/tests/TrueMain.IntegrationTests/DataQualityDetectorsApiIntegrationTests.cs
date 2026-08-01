using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Core.Lol.Map;
using Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Covers the automated detectors (#924). The unit tests pin the judgement; these pin
/// the measurements, which are the half that cannot be checked without Postgres: the
/// canonical-key grouping is raw SQL shared with the ingestor's repair, and the orphan
/// sample is a lateral whose whole point is to read an index range instead of the table.
///
/// <para>
/// The seed reproduces the #911 shape in all three audited dimensions — a rune page
/// whose secondary perks are swapped, a spell pair stored in the player's order, and a
/// starter basket holding the same items in another sequence. Each is a permutation the
/// UNIQUE index accepts and the canonical key must collapse.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class DataQualityDetectorsApiIntegrationTests(PostgresFixture fixture)
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;
    private readonly PostgresFixture _fixture = fixture;

    [Fact]
    public async Task GetDetectors_CollapsesEveryPermutationOntoOneCanonicalKey()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedDuplicateDimensionsAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        var payload = await client.GetFromJsonAsync<DetectorsContract>("/ops/data-quality/detectors");

        payload.Should().NotBeNull();
        var duplicates = payload!.Detectors.Single(detector => detector.Key == "duplicateDimensionRows");

        // One group per audited dimension: the swapped rune page, the swapped spell pair
        // and the re-ordered starter basket each collapse onto a key they now share.
        duplicates.Count.Should().Be(3);
        duplicates.Status.Should().Be("red");

        var byTable = duplicates.Rows.ToDictionary(row => row.Label, StringComparer.Ordinal);
        byTable["champion_dim_rune_pages"].Value.Should().Be(1);
        byTable["champion_dim_spell_pairs"].Value.Should().Be(1);
        byTable["champion_dim_starter_items"].Value.Should().Be(1);

        // The leading indicator: a row stored outside canonical order. Reported for the
        // two dimensions where canonical order is expressible in SQL, and explicitly not
        // claimed for starter items, whose canonical order depends on patch prices.
        byTable["champion_dim_rune_pages"].Note.Should().Contain("1 row(s) stored outside canonical order");
        byTable["champion_dim_spell_pairs"].Note.Should().Contain("1 row(s) stored outside canonical order");
        byTable["champion_dim_starter_items"].Note.Should().Contain("not checkable in SQL");
    }

    [Fact]
    public async Task GetDetectors_ReportsAGreenDuplicateCard_WhenEveryRowIsCanonical()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedCanonicalDimensionsAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        var payload = await client.GetFromJsonAsync<DetectorsContract>("/ops/data-quality/detectors");

        var duplicates = payload!.Detectors.Single(detector => detector.Key == "duplicateDimensionRows");
        duplicates.Count.Should().Be(0);
        duplicates.Status.Should().Be("green");
    }

    [Fact]
    public async Task GetDetectors_WordsTheDuplicateHeadlineFromTheVerdict_NotFromTheDuplicateCount()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedNonCanonicalOnlyAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        var payload = await client.GetFromJsonAsync<DetectorsContract>("/ops/data-quality/detectors");

        var duplicates = payload!.Detectors.Single(detector => detector.Key == "duplicateDimensionRows");

        // The leading indicator alone: rows stored the player's way round, no split yet.
        // The card must react — this is how #911 comes back — and its sentence must not
        // answer the amber badge with "every row is unique".
        duplicates.Count.Should().Be(0);
        duplicates.Status.Should().Be("amber");
        duplicates.Headline.Should().Contain("outside canonical order");
        duplicates.Headline.Should().NotContain("unique under its canonical key");
    }

    [Fact]
    public async Task GetDetectors_WordsTheSanityHeadlineFromTheVerdict_WhenOnlyZeroSampleRowsExist()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedZeroSampleScopeAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        var payload = await client.GetFromJsonAsync<DetectorsContract>("/ops/data-quality/detectors");

        var sanity = payload!.Detectors.Single(detector => detector.Key == "rowSanity");

        // Nothing is arithmetically impossible here, so the count is 0 — but a zero-sample
        // row still raises the card, and the sentence has to talk about what raised it.
        sanity.Count.Should().Be(0);
        sanity.Status.Should().Be("amber");
        sanity.Headline.Should().Contain("carry no games");
        sanity.Headline.Should().NotContain("No aggregate row contradicts");
    }

    [Fact]
    public async Task GetDetectors_MeasuresTheOrphanShareFromTheNewestMatchesPerPlatform()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedOrphanSampleAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        var payload = await client.GetFromJsonAsync<DetectorsContract>("/ops/data-quality/detectors");

        var orphans = payload!.Detectors.Single(detector => detector.Key == "orphanParticipants");
        var byPlatform = orphans.Rows.ToDictionary(row => row.Label, StringComparer.Ordinal);

        // EUW1: 4 matches × 10 participants, 9 of each untracked → 90%, the healthy
        // resting state rather than an anomaly.
        byPlatform.Should().ContainKey("EUW1");
        byPlatform["EUW1"].Value.Should().BeApproximately(90, 0.01);
        byPlatform["EUW1"].Status.Should().Be("green");

        // KR: every participant untracked → 100%, which is the failure the card exists
        // for: nothing is being attributed to a tracked account any more.
        byPlatform.Should().ContainKey("KR");
        byPlatform["KR"].Value.Should().Be(100);
        byPlatform["KR"].Status.Should().Be("red");

        // Harvest never ran in this seed, so its row must read unknown — not green.
        byPlatform["Harvest (last success)"].Status.Should().Be("unknown");

        // NA1 has a tracked account and no match at all. It drops out of both the lateral
        // and the GROUP BY, so without an explicit row it would vanish from the card —
        // and "ingestion never started on this platform" would read as nothing to report.
        byPlatform.Should().ContainKey("NA1");
        byPlatform["NA1"].Status.Should().Be("unknown");

        var lag = payload.Detectors.Single(detector => detector.Key == "ingestionLag");
        lag.Rows.Should().Contain(row => row.Label == "NA1" && row.Status == "unknown");
    }

    [Fact]
    public async Task GetDetectors_ReturnsEveryDetectorWithItsThresholds_OnAnEmptyDatabase()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        var payload = await client.GetFromJsonAsync<DetectorsContract>("/ops/data-quality/detectors");

        payload.Should().NotBeNull();
        payload!.Detectors.Select(detector => detector.Key).Should().BeEquivalentTo(
        [
            "duplicateDimensionRows",
            "aggregateFreshness",
            "orphanParticipants",
            "ingestionLag",
            "rowSanity"
        ]);

        // Every card states the line it drew, so a colour is never the only explanation.
        payload.Detectors.Should().OnlyContain(detector => detector.Thresholds.Count > 0);

        // Every level says which side of it is the bad one, because the panel prints the
        // line in words and cannot infer the direction from the number.
        payload.Detectors
            .SelectMany(detector => detector.Thresholds)
            .Should().OnlyContain(threshold => threshold.Direction == "above" || threshold.Direction == "below");

        // The one floor among them: a patch is anomalous when its volume falls *below* a
        // share of the median. Printed as a ceiling it would read as the opposite rule.
        payload.Detectors.Single(detector => detector.Key == "rowSanity")
            .Thresholds.Single(threshold => threshold.Unit == "ratio")
            .Direction.Should().Be("below");

        // Nothing has ever run on an empty database, so freshness cannot be green — and
        // its headline must not claim success either. A card whose colour says "not
        // measured" while its sentence says "everything completed" is the dashboard
        // lying, which is the one failure mode these detectors exist to avoid.
        var freshness = payload.Detectors.Single(detector => detector.Key == "aggregateFreshness");
        freshness.Status.Should().Be("unknown");
        freshness.Headline.Should().Contain("unmeasured");

        // Same for ingestion: "every platform is fresh" is vacuously true with no
        // platforms, so an empty corpus reads unknown rather than green.
        payload.Detectors.Single(detector => detector.Key == "ingestionLag")
            .Status.Should().Be("unknown");
    }

    [Fact]
    public async Task GetAggregateFreshness_RanksTheStalestChampionFirst()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAggregateScopesAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        var payload = await client.GetFromJsonAsync<FreshnessContract>("/ops/data-quality/aggregate-freshness");

        payload.Should().NotBeNull();
        payload!.Champions.Should().NotBeEmpty();
        payload.Champions[0].ChampionId.Should().Be(266, "the stalest champion leads the breakdown");
        payload.Champions[0].Status.Should().Be("red");
        payload.StaleChampionCount.Should().Be(1);
        payload.ChampionCount.Should().Be(2);
    }

    [Fact]
    public async Task DetectorEndpoints_RejectAnUnauthenticatedCaller()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        (await client.GetAsync(new Uri("/ops/data-quality/detectors", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync(new Uri("/ops/data-quality/aggregate-freshness", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- seeds ---------------------------------------------------------------

    private async Task SeedDuplicateDimensionsAsync()
    {
        await using var db = _fixture.CreateDbContext();

        // The #911 pair: identical pages whose two secondary perks are swapped. The
        // UNIQUE index over the stored columns accepts both.
        db.ChampionDimRunePages.Add(BuildRunePage(secondary1: 8444, secondary2: 8451));
        db.ChampionDimRunePages.Add(BuildRunePage(secondary1: 8451, secondary2: 8444));

        // Flash+Ignite stored both ways round.
        db.ChampionDimSpellPairs.Add(new ChampionDimSpellPair { Id = Guid.NewGuid(), Spell1Id = 4, Spell2Id = 14 });
        db.ChampionDimSpellPairs.Add(new ChampionDimSpellPair { Id = Guid.NewGuid(), Spell1Id = 14, Spell2Id = 4 });

        // The same basket keyed two ways — what a Riot re-pricing does to the
        // price-ordered StarterItemsKey.
        db.ChampionDimStarterItems.Add(new ChampionDimStarterItems
        {
            Id = Guid.NewGuid(),
            StarterItemsKey = "1055|2003",
            StarterItems = [1055, 2003]
        });
        db.ChampionDimStarterItems.Add(new ChampionDimStarterItems
        {
            Id = Guid.NewGuid(),
            StarterItemsKey = "2003|1055",
            StarterItems = [2003, 1055]
        });

        await db.SaveChangesAsync();
    }

    private async Task SeedCanonicalDimensionsAsync()
    {
        await using var db = _fixture.CreateDbContext();

        db.ChampionDimRunePages.Add(BuildRunePage(secondary1: 8444, secondary2: 8451));
        db.ChampionDimSpellPairs.Add(new ChampionDimSpellPair { Id = Guid.NewGuid(), Spell1Id = 4, Spell2Id = 14 });
        db.ChampionDimStarterItems.Add(new ChampionDimStarterItems
        {
            Id = Guid.NewGuid(),
            StarterItemsKey = "1055|2003",
            StarterItems = [1055, 2003]
        });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// One rune page and one spell pair stored in the player's order, each without a
    /// canonical twin: the leading indicator without any duplicate yet.
    /// </summary>
    private async Task SeedNonCanonicalOnlyAsync()
    {
        await using var db = _fixture.CreateDbContext();

        db.ChampionDimRunePages.Add(BuildRunePage(secondary1: 8451, secondary2: 8444));
        db.ChampionDimSpellPairs.Add(new ChampionDimSpellPair { Id = Guid.NewGuid(), Spell1Id = 14, Spell2Id = 4 });

        await db.SaveChangesAsync();
    }

    /// <summary>A scope row that was written with no games behind it.</summary>
    private async Task SeedZeroSampleScopeAsync()
    {
        await using var db = _fixture.CreateDbContext();

        var account = BuildAccount("EUW1");
        db.RiotAccounts.Add(account);

        var scope = BuildScope(account.Id, championId: 103, DateTime.UtcNow);
        scope.Games = 0;
        scope.Wins = 0;
        db.ChampionAggregateScopes.Add(scope);

        await db.SaveChangesAsync();
    }

    private async Task SeedOrphanSampleAsync()
    {
        var now = DateTime.UtcNow;
        await using var db = _fixture.CreateDbContext();

        // The platforms the sample iterates come from riot_accounts, not from a DISTINCT
        // over matches — so a platform with no tracked account is not sampled at all.
        var euwAccount = BuildAccount("EUW1");
        db.RiotAccounts.Add(euwAccount);
        db.RiotAccounts.Add(BuildAccount("KR"));
        // Tracked, but never ingested: must surface as unknown rather than disappear.
        db.RiotAccounts.Add(BuildAccount("NA1"));

        for (var index = 0; index < 4; index++)
        {
            var matchId = $"DET_EUW_{index}";
            db.Matches.Add(BuildMatch(matchId, "EUW1", now.AddHours(-index)));
            for (var participantId = 1; participantId <= 10; participantId++)
            {
                // One tracked row in ten: a tracked player's own game, with nine
                // untracked opponents and team-mates.
                db.MatchParticipants.Add(BuildParticipant(
                    matchId,
                    participantId,
                    participantId == 1 ? euwAccount.Id : null));
            }
        }

        for (var index = 0; index < 4; index++)
        {
            var matchId = $"DET_KR_{index}";
            db.Matches.Add(BuildMatch(matchId, "KR", now.AddHours(-index)));
            for (var participantId = 1; participantId <= 10; participantId++)
            {
                db.MatchParticipants.Add(BuildParticipant(matchId, participantId, riotAccountId: null));
            }
        }

        await db.SaveChangesAsync();
    }

    private async Task SeedAggregateScopesAsync()
    {
        var now = DateTime.UtcNow;
        await using var db = _fixture.CreateDbContext();

        var account = BuildAccount("EUW1");
        db.RiotAccounts.Add(account);

        // Aatrox aggregated three days ago (past the 24 h red line), Ahri an hour ago.
        db.ChampionAggregateScopes.Add(BuildScope(account.Id, championId: 266, now.AddDays(-3)));
        db.ChampionAggregateScopes.Add(BuildScope(account.Id, championId: 103, now.AddHours(-1)));

        await db.SaveChangesAsync();
    }

    // ---- builders ------------------------------------------------------------

    private static ChampionDimRunePage BuildRunePage(int secondary1, int secondary2) => new()
    {
        Id = Guid.NewGuid(),
        PrimaryStyleId = 8400,
        PrimaryKeystoneId = 8437,
        PrimaryPerk1Id = 8446,
        PrimaryPerk2Id = 8473,
        PrimaryPerk3Id = 8242,
        SecondaryStyleId = 8300,
        SecondaryPerk1Id = secondary1,
        SecondaryPerk2Id = secondary2,
        StatOffense = 5008,
        StatFlex = 5008,
        StatDefense = 5001
    };

    private static RiotAccount BuildAccount(string platformId) => new()
    {
        Id = Guid.NewGuid(),
        Puuid = $"{platformId}-{Guid.NewGuid():N}",
        GameName = $"{platformId}Main",
        TagLine = platformId,
        PlatformId = platformId,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static Match BuildMatch(string id, string platformId, DateTime gameStart) => new()
    {
        Id = id,
        PlatformId = platformId,
        QueueId = (int)LolQueueId.RankedSoloDuo,
        MapId = (int)LolMapId.SummonersRift,
        GameMode = "CLASSIC",
        GameType = "MATCHED_GAME",
        GameStartTimeUtc = gameStart,
        GameDurationSeconds = 1800,
        GameVersion = "16.15.1.1",
        CreatedAtUtc = DateTime.UtcNow,
        TimelineIngested = true
    };

    private static MatchParticipant BuildParticipant(string matchId, int participantId, Guid? riotAccountId) => new()
    {
        Id = Guid.NewGuid(),
        MatchId = matchId,
        ParticipantId = participantId,
        Puuid = $"{matchId}-p{participantId}",
        RiotAccountId = riotAccountId,
        SummonerName = $"{matchId}-p{participantId}",
        SummonerLevel = 100,
        ChampionId = 100 + participantId,
        TeamId = participantId <= 5 ? 100 : 200,
        TeamPosition = "MIDDLE",
        IndividualPosition = "MIDDLE",
        Lane = "MIDDLE",
        Role = "NONE",
        Win = participantId <= 5,
        ItemEvents = [],
        SkillEvents = []
    };

    private static ChampionAggregateScope BuildScope(Guid accountId, int championId, DateTime aggregatedAt) => new()
    {
        Id = Guid.NewGuid(),
        RiotAccountId = accountId,
        ChampionId = championId,
        GameVersion = "16.15.1",
        PlatformId = "EUW1",
        QueueId = (int)LolQueueId.RankedSoloDuo,
        Position = "MIDDLE",
        EloBracket = "GOLD",
        Games = 10,
        Wins = 5,
        AggregatedAtUtc = aggregatedAt
    };

    private static HttpClient CreateAuthedClient(ApiWebApplicationFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Ops-Key", OpsApiKey);
        return client;
    }

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(fixture);

    private sealed class DetectorsContract
    {
        public IReadOnlyList<DetectorContract> Detectors { get; init; } = [];
    }

    private sealed class DetectorContract
    {
        public string Key { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string Headline { get; init; } = string.Empty;
        public long? Count { get; init; }
        public IReadOnlyList<DetectorRowContract> Rows { get; init; } = [];
        public IReadOnlyList<ThresholdContract> Thresholds { get; init; } = [];
    }

    private sealed class DetectorRowContract
    {
        public string Label { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public double? Value { get; init; }
        public string? Note { get; init; }
    }

    private sealed class ThresholdContract
    {
        public string Label { get; init; } = string.Empty;
        public double? Amber { get; init; }
        public double? Red { get; init; }
        public string Direction { get; init; } = string.Empty;
        public string Unit { get; init; } = string.Empty;
    }

    private sealed class FreshnessContract
    {
        public IReadOnlyList<ChampionFreshnessContract> Champions { get; init; } = [];
        public int ChampionCount { get; init; }
        public int StaleChampionCount { get; init; }
    }

    private sealed class ChampionFreshnessContract
    {
        public int ChampionId { get; init; }
        public string Status { get; init; } = string.Empty;
        public double AgeHours { get; init; }
    }
}
