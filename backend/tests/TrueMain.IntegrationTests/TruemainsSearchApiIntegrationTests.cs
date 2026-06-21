using System.Net;
using System.Net.Http.Json;
using Core.Lol.Ranking;
using Data.Entities;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class TruemainsSearchApiIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public TruemainsSearchApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Search_matches_game_name_substring_case_insensitively_and_ranks_exact_first()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        await using (var db = _fixture.CreateDbContext())
        {
            var phantasm = Account("phantasm", "Phantasm", "EUW1");
            var phantomLord = Account("phantom", "PhantomLord", "EUW1");
            var unrelated = Account("unrelated", "Garen", "EUW1");

            db.RiotAccounts.AddRange(phantasm, phantomLord, unrelated);
            db.RankSnapshots.AddRange(
                Snapshot(phantasm, "DIAMOND", "II", 50, now),
                Snapshot(phantomLord, "MASTER", "I", 200, now),
                Snapshot(unrelated, "DIAMOND", "I", 80, now));
            db.MainChampionStats.AddRange(
                MainStat("phantasm", "EUW1", 157, isMain: true),
                MainStat("phantom", "EUW1", 86, isMain: true),
                MainStat("unrelated", "EUW1", 86, isMain: true));

            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // Lowercase substring "phant" matches both Phantasm and PhantomLord but
        // not Garen — case-insensitive, served by the trigram index. Neither is
        // an exact name match, so the secondary sort decides the order: higher
        // score first, i.e. PhantomLord (MASTER) ahead of Phantasm (DIAMOND).
        // Asserting the order (not just the set) locks the ThenByDescending(Score).
        var response = await client.GetFromJsonAsync<SearchResponse>("/truemains/search?q=phant");
        response!.Results.Select(r => r.Identity.GameName)
            .Should().Equal("PhantomLord", "Phantasm");

        // An exact (case-insensitive) name match sorts ahead of mere substring
        // hits, even when the substring hit is higher ranked.
        var exact = await client.GetFromJsonAsync<SearchResponse>("/truemains/search?q=PHANTASM");
        exact!.Results.Should().NotBeEmpty();
        exact.Results[0].Identity.GameName.Should().Be("Phantasm");
    }

    [Fact]
    public async Task Search_with_tag_narrows_to_the_matching_tag_line()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        // Same game name on two regions → two distinct Riot ids. The tag in the
        // query (Name#TAG) must pick exactly one.
        await using (var db = _fixture.CreateDbContext())
        {
            var euw = Account("twin-euw", "Twin", "EUW1", tagLine: "EUW");
            var na = Account("twin-na", "Twin", "NA1", tagLine: "NA1");

            db.RiotAccounts.AddRange(euw, na);
            db.RankSnapshots.AddRange(
                Snapshot(euw, "DIAMOND", "I", 50, now),
                Snapshot(na, "DIAMOND", "I", 50, now));
            db.MainChampionStats.AddRange(
                MainStat("twin-euw", "EUW1", 157, isMain: true),
                MainStat("twin-na", "NA1", 157, isMain: true));

            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var bare = await client.GetFromJsonAsync<SearchResponse>("/truemains/search?q=Twin");
        bare!.Results.Should().HaveCount(2);

        var tagged = await client.GetFromJsonAsync<SearchResponse>("/truemains/search?q=Twin%23NA1");
        tagged!.Results.Should().ContainSingle();
        tagged.Results[0].Identity.TagLine.Should().Be("NA1");
        tagged.Results[0].Identity.PlatformId.Should().Be("NA1");
        tagged.Results[0].Region.Should().Be("americas");
    }

    [Fact]
    public async Task Search_excludes_unranked_and_non_main_accounts()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        await using (var db = _fixture.CreateDbContext())
        {
            var realMain = Account("real", "Searchy", "EUW1");
            var noMain = Account("nomain", "SearchyTwo", "EUW1");
            var unranked = Account("unranked", "SearchyThree", "EUW1");

            db.RiotAccounts.AddRange(realMain, noMain, unranked);
            // realMain: ranked + IsMain → eligible.
            db.RankSnapshots.Add(Snapshot(realMain, "DIAMOND", "I", 80, now));
            db.MainChampionStats.Add(MainStat("real", "EUW1", 157, isMain: true));
            // noMain: ranked but only an IsMain=false row → excluded.
            db.RankSnapshots.Add(Snapshot(noMain, "DIAMOND", "I", 80, now));
            db.MainChampionStats.Add(MainStat("nomain", "EUW1", 86, isMain: false));
            // unranked: IsMain but no snapshot → Score stays null → excluded.
            db.MainChampionStats.Add(MainStat("unranked", "EUW1", 157, isMain: true));

            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.GetFromJsonAsync<SearchResponse>("/truemains/search?q=Searchy");
        response!.Results.Should().ContainSingle();
        response.Results[0].Identity.GameName.Should().Be("Searchy");
        response.Results[0].Ranked.Should().NotBeNull();
        response.Results[0].Ranked!.Tier.Should().Be("DIAMOND");
    }

    [Theory]
    [InlineData("/truemains/search")]
    [InlineData("/truemains/search?q=")]
    [InlineData("/truemains/search?q=a")]
    // Name part below the floor even though a tag is present — a tag must not
    // smuggle the query past the min-length guard.
    [InlineData("/truemains/search?q=a%23NA1")]
    [InlineData("/truemains/search?q=%20%20")]
    public async Task Search_returns_empty_200_for_too_short_or_missing_query(string url)
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<SearchResponse>();
        payload!.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_returns_empty_200_for_an_over_long_query()
    {
        // A query far longer than any real Riot id is rejected before it reaches
        // EscapeLike / the ILIKE — a normal empty 200, never a 500.
        await _fixture.ResetDatabaseAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var longQuery = new string('a', 100);
        var response = await client.GetAsync($"/truemains/search?q={longQuery}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<SearchResponse>();
        payload!.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_treats_like_metacharacters_as_literals()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        // "100%Real" contains a literal '%'; "RealDeal" is a decoy that shares
        // the "Real" substring but not "%Real". A query of "%Real" must match
        // only the first — if the '%' leaked through unescaped it would act as a
        // wildcard and pull in the decoy too, so the decoy is what makes this
        // assertion meaningful rather than a single-row tautology.
        await using (var db = _fixture.CreateDbContext())
        {
            var literal = Account("literal", "100%Real", "EUW1");
            var decoy = Account("decoy", "RealDeal", "EUW1");

            db.RiotAccounts.AddRange(literal, decoy);
            db.RankSnapshots.AddRange(
                Snapshot(literal, "GOLD", "I", 20, now),
                Snapshot(decoy, "GOLD", "I", 20, now));
            db.MainChampionStats.AddRange(
                MainStat("literal", "EUW1", 157, isMain: true),
                MainStat("decoy", "EUW1", 86, isMain: true));

            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.GetFromJsonAsync<SearchResponse>("/truemains/search?q=%25Real");
        response!.Results.Should().ContainSingle("the literal '%' must not widen the match to the decoy");
        response.Results[0].Identity.GameName.Should().Be("100%Real");
    }

    [Fact]
    public async Task Search_treats_underscore_metacharacter_as_literal()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        // EscapeLike neutralises '_' as well as '%'. "100_Real" has a literal
        // '_'; "100XReal" is a decoy where the 'X' would be caught by an
        // unescaped '_' (LIKE's single-char wildcard). A "100_Real" query must
        // match only the literal one — the decoy is what makes this bite.
        await using (var db = _fixture.CreateDbContext())
        {
            var literal = Account("underscore", "100_Real", "EUW1");
            var decoy = Account("wildcard", "100XReal", "EUW1");

            db.RiotAccounts.AddRange(literal, decoy);
            db.RankSnapshots.AddRange(
                Snapshot(literal, "GOLD", "I", 20, now),
                Snapshot(decoy, "GOLD", "I", 20, now));
            db.MainChampionStats.AddRange(
                MainStat("underscore", "EUW1", 157, isMain: true),
                MainStat("wildcard", "EUW1", 86, isMain: true));

            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.GetFromJsonAsync<SearchResponse>("/truemains/search?q=100_Real");
        response!.Results.Should().ContainSingle("a literal '_' must not act as a single-char wildcard");
        response.Results[0].Identity.GameName.Should().Be("100_Real");
    }

    [Fact]
    public async Task Search_tag_metacharacters_are_treated_as_literals()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        // Two "Zed"s on different tags. A `Zed#%` query must match neither —
        // no tag line is literally "%". If the tag were fed to a LIKE without
        // escaping, '%' would act as a wildcard and pull in both, so the second
        // account is what makes this assertion bite.
        await using (var db = _fixture.CreateDbContext())
        {
            var euw = Account("zed-euw", "Zed", "EUW1", tagLine: "EUW");
            var na = Account("zed-na", "Zed", "NA1", tagLine: "NA1");

            db.RiotAccounts.AddRange(euw, na);
            db.RankSnapshots.AddRange(
                Snapshot(euw, "DIAMOND", "I", 50, now),
                Snapshot(na, "DIAMOND", "I", 50, now));
            db.MainChampionStats.AddRange(
                MainStat("zed-euw", "EUW1", 238, isMain: true),
                MainStat("zed-na", "NA1", 238, isMain: true));

            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // Wildcard tag must not widen the filter…
        var wildcard = await client.GetFromJsonAsync<SearchResponse>("/truemains/search?q=Zed%23%25");
        wildcard!.Results.Should().BeEmpty("a literal '%' tag matches no real tag line");

        // …while an exact tag still resolves to the one account.
        var exact = await client.GetFromJsonAsync<SearchResponse>("/truemains/search?q=Zed%23NA1");
        exact!.Results.Should().ContainSingle();
        exact.Results[0].Identity.TagLine.Should().Be("NA1");
    }

    [Fact]
    public async Task Search_clamps_the_result_limit()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        // 30 eligible accounts that all share the "Clamp" substring — enough to
        // exercise both the explicit-limit cap and the MaxLimit ceiling.
        await using (var db = _fixture.CreateDbContext())
        {
            for (var i = 0; i < 30; i++)
            {
                var id = $"clamp-{i}";
                var account = Account(id, $"Clamp{i:D2}", "EUW1");
                db.RiotAccounts.Add(account);
                db.RankSnapshots.Add(Snapshot(account, "DIAMOND", "I", 50 + i, now));
                db.MainChampionStats.Add(MainStat(id, "EUW1", 157, isMain: true));
            }

            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // Explicit small limit caps the slice.
        var three = await client.GetFromJsonAsync<SearchResponse>("/truemains/search?q=Clamp&limit=3");
        three!.Results.Should().HaveCount(3);

        // An over-large limit is clamped to MaxLimit (25), not honoured verbatim.
        var hundred = await client.GetFromJsonAsync<SearchResponse>("/truemains/search?q=Clamp&limit=100");
        hundred!.Results.Should().HaveCount(25);

        // No limit falls back to the default (10).
        var noLimit = await client.GetFromJsonAsync<SearchResponse>("/truemains/search?q=Clamp");
        noLimit!.Results.Should().HaveCount(10);
    }

    private static RiotAccount Account(string puuid, string gameName, string platformId, string? tagLine = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Puuid = puuid,
            GameName = gameName,
            TagLine = tagLine ?? platformId,
            PlatformId = platformId,
            ProfileIconId = 1,
            SummonerLevel = 100,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            LastMatchIngestAtUtc = DateTime.UtcNow,
        };

    private static RankSnapshot Snapshot(RiotAccount account, string tier, string division, int leaguePoints, DateTime now)
    {
        // Mirror the ingestion writer: the snapshot's rank drives the account's
        // denormalised Score, which is the "is ranked" gate the search applies.
        account.Score = RankScore.Compute(tier, division, leaguePoints);
        return new()
        {
            Id = Guid.NewGuid(),
            RiotAccount = account,
            CapturedAtUtc = now,
            Tier = tier,
            Division = division,
            LeaguePoints = leaguePoints,
            Wins = 50,
            Losses = 50,
        };
    }

    private static MainChampionStat MainStat(string puuid, string platformId, int championId, bool isMain)
        => new()
        {
            Id = Guid.NewGuid(),
            PlatformId = platformId,
            Puuid = puuid,
            ChampionId = championId,
            TotalMatches = 100,
            ChampionMatches = 100,
            PlayRate = 1d,
            IsMain = isMain,
            IsOtp = false,
            PrimaryPosition = "MIDDLE",
            PositionBreakdown = [new PositionStat { Position = "MIDDLE", Games = 50, Rate = 1d }],
            CalculatedAtUtc = DateTime.UtcNow,
        };

    private ApiWebApplicationFactory CreateFactory() => new(_fixture);

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [
                new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420"),
            ]);
}
