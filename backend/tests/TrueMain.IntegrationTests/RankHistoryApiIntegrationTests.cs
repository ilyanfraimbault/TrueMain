using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.IntegrationTests;

/// <summary>
/// End-to-end cover for <c>GET /truemains/{nameTag}/rank-history</c>, the LP curve on the
/// profile. What the chart draws is a line, so the two properties that matter are the ones
/// a reader would see broken instantly: the points come back oldest-first, and the window
/// really is the one asked for — with the 90-day default applied when nothing is asked.
/// </summary>
/// <remarks>
/// The window boundaries are seeded with days of margin on either side of the cutoff, never
/// on it: the service builds its lower bound from <c>DateTime.UtcNow</c> at request time,
/// so a row placed exactly on the edge would flip with the wall clock (the failure mode
/// TEST-9 names elsewhere in the suite).
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class RankHistoryApiIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public RankHistoryApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Returns_404_for_an_unknown_account()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/truemains/Unknown-NA1/rank-history");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("NoHyphen")]
    [InlineData("-LeadingHyphen")]
    [InlineData("TrailingHyphen-")]
    public async Task Returns_404_for_a_malformed_name_tag(string nameTag)
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/truemains/{nameTag}/rank-history");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Returns_an_empty_series_rather_than_404_for_a_known_account_with_no_snapshots()
    {
        await _fixture.ResetDatabaseAsync();

        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(Account("silent-puuid", "Silent"));
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/truemains/Silent-EUW1/rank-history");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // 404 here would make the profile say "no such player" about a player it is
        // already rendering; an unranked account with no captures is a legitimate
        // empty curve.
        var payload = await response.Content.ReadFromJsonAsync<RankHistoryReadModel>();
        payload.Should().NotBeNull();
        payload!.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Returns_the_snapshots_oldest_first_with_the_chart_contract_shape()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        var account = Account("climber-puuid", "Climber");
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(account);
            // Inserted newest-first on purpose: the ordering under test is the query's,
            // not the insertion order Postgres happens to return.
            db.RankSnapshots.Add(Snapshot(account, now.AddDays(-1), "DIAMOND", "IV", 12));
            db.RankSnapshots.Add(Snapshot(account, now.AddDays(-10), "EMERALD", "I", 88));
            db.RankSnapshots.Add(Snapshot(account, now.AddDays(-20), "EMERALD", "II", 41));
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/truemains/Climber-EUW1/rank-history");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["entries"]);
        document.RootElement.GetProperty("entries")[0].EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["capturedAtUtc", "tier", "division", "leaguePoints"]);

        var payload = await response.Content.ReadFromJsonAsync<RankHistoryReadModel>();
        payload.Should().NotBeNull();

        // Oldest-first: a chart plotted straight from this array must run left to right
        // through time.
        payload!.Entries.Should().HaveCount(3);
        payload.Entries.Should().BeInAscendingOrder(entry => entry.CapturedAtUtc);
        payload.Entries.Select(entry => entry.LeaguePoints).Should().Equal(41, 88, 12);
        payload.Entries.Select(entry => entry.Tier).Should().Equal("EMERALD", "EMERALD", "DIAMOND");
        payload.Entries.Select(entry => entry.Division).Should().Equal("II", "I", "IV");
    }

    [Fact]
    public async Task Defaults_to_a_90_day_window_when_days_is_absent_zero_or_negative()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        var account = Account("veteran-puuid", "Veteran");
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(account);
            db.RankSnapshots.Add(Snapshot(account, now.AddDays(-120), "GOLD", "I", 10));
            db.RankSnapshots.Add(Snapshot(account, now.AddDays(-95), "PLATINUM", "IV", 20));
            db.RankSnapshots.Add(Snapshot(account, now.AddDays(-85), "PLATINUM", "III", 30));
            db.RankSnapshots.Add(Snapshot(account, now.AddDays(-2), "PLATINUM", "I", 40));
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        foreach (var query in new[] { string.Empty, "?days=0", "?days=-30" })
        {
            var payload = await client.GetFromJsonAsync<RankHistoryReadModel>(
                $"/truemains/Veteran-EUW1/rank-history{query}");

            // 0 and a negative are not "no window" and not "one day": they mean the
            // caller said nothing useful, which is the default.
            payload!.Entries.Select(entry => entry.LeaguePoints)
                .Should().Equal([30, 40], $"'{query}' must fall back to the 90-day default");
        }
    }

    [Fact]
    public async Task Narrows_the_window_to_the_requested_number_of_days()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        var account = Account("recent-puuid", "Recent");
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(account);
            db.RankSnapshots.Add(Snapshot(account, now.AddDays(-20), "GOLD", "II", 55));
            db.RankSnapshots.Add(Snapshot(account, now.AddDays(-3), "GOLD", "I", 65));
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var payload = await client.GetFromJsonAsync<RankHistoryReadModel>(
            "/truemains/Recent-EUW1/rank-history?days=7");

        payload!.Entries.Should().ContainSingle().Which.LeaguePoints.Should().Be(65);
    }

    [Fact]
    public async Task Clamps_an_oversized_window_to_two_years_instead_of_scanning_everything()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        var account = Account("ancient-puuid", "Ancient");
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(account);
            // Straddles the 730-day ceiling: the older row is what the clamp exists to
            // keep out of a hostile `days=999999`.
            db.RankSnapshots.Add(Snapshot(account, now.AddDays(-800), "BRONZE", "III", 5));
            db.RankSnapshots.Add(Snapshot(account, now.AddDays(-700), "SILVER", "II", 15));
            db.RankSnapshots.Add(Snapshot(account, now.AddDays(-1), "GOLD", "IV", 25));
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var payload = await client.GetFromJsonAsync<RankHistoryReadModel>(
            "/truemains/Ancient-EUW1/rank-history?days=999999");

        payload!.Entries.Select(entry => entry.LeaguePoints).Should().Equal(15, 25);
    }

    [Fact]
    public async Task Serves_only_the_requested_account_when_two_players_share_a_game_name()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        var wanted = Account("wanted-puuid", "Twin", tagLine: "EUW1");
        var other = Account("other-puuid", "Twin", tagLine: "NA1");
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.AddRange(wanted, other);
            db.RankSnapshots.Add(Snapshot(wanted, now.AddDays(-5), "MASTER", string.Empty, 350));
            db.RankSnapshots.Add(Snapshot(other, now.AddDays(-5), "IRON", "IV", 0));
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var payload = await client.GetFromJsonAsync<RankHistoryReadModel>(
            "/truemains/Twin-EUW1/rank-history");

        // The tag line is half the identity: matching on the game name alone would draw
        // another player's curve on this profile.
        var entry = payload!.Entries.Should().ContainSingle().Subject;
        entry.Tier.Should().Be("MASTER");
        entry.LeaguePoints.Should().Be(350);
        // Apex tiers carry no division; the read model passes the empty string through
        // rather than inventing an "I".
        entry.Division.Should().BeEmpty();
    }

    [Fact]
    public async Task Prefers_the_most_recently_ingested_account_when_a_riot_id_was_reused()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        // A Riot ID freed and re-registered: two rows share (gameName, tagLine) and the
        // profile resolves the one we ingested last. Rank history has to resolve the same
        // one, or the curve belongs to a different player than the header above it.
        var stale = Account("stale-puuid", "Reused");
        stale.LastMatchIngestAtUtc = now.AddDays(-200);
        stale.UpdatedAtUtc = now.AddDays(-200);

        var current = Account("current-puuid", "Reused");
        current.LastMatchIngestAtUtc = now.AddHours(-1);
        current.UpdatedAtUtc = now.AddHours(-1);

        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.AddRange(stale, current);
            db.RankSnapshots.Add(Snapshot(stale, now.AddDays(-3), "IRON", "IV", 1));
            db.RankSnapshots.Add(Snapshot(current, now.AddDays(-3), "DIAMOND", "II", 74));
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var payload = await client.GetFromJsonAsync<RankHistoryReadModel>(
            "/truemains/Reused-EUW1/rank-history");

        payload!.Entries.Should().ContainSingle().Which.Tier.Should().Be("DIAMOND");
    }

    [Fact]
    public async Task Keeps_a_flat_stretch_as_repeated_points_rather_than_collapsing_it()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        var account = Account("plateau-puuid", "Plateau");
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(account);
            // Three days at the same LP. The writer stores at most one row per UTC day, so
            // these are three real captures, not duplicates: dropping the middle ones would
            // make a week-long plateau look like a single point.
            db.RankSnapshots.Add(Snapshot(account, now.AddDays(-4), "GOLD", "II", 47));
            db.RankSnapshots.Add(Snapshot(account, now.AddDays(-3), "GOLD", "II", 47));
            db.RankSnapshots.Add(Snapshot(account, now.AddDays(-2), "GOLD", "II", 47));
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var payload = await client.GetFromJsonAsync<RankHistoryReadModel>(
            "/truemains/Plateau-EUW1/rank-history");

        payload!.Entries.Should().HaveCount(3);
        payload.Entries.Should().OnlyContain(entry => entry.LeaguePoints == 47);
    }

    private static RiotAccount Account(string puuid, string gameName, string tagLine = "EUW1")
        => new()
        {
            Id = Guid.NewGuid(),
            Puuid = puuid,
            GameName = gameName,
            TagLine = tagLine,
            PlatformId = "EUW1",
            ProfileIconId = 1,
            SummonerLevel = 100,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-365),
            UpdatedAtUtc = DateTime.UtcNow,
            LastMatchIngestAtUtc = DateTime.UtcNow,
        };

    private static RankSnapshot Snapshot(
        RiotAccount account,
        DateTime capturedAtUtc,
        string tier,
        string division,
        int leaguePoints)
        => new()
        {
            Id = Guid.NewGuid(),
            RiotAccountId = account.Id,
            CapturedAtUtc = capturedAtUtc,
            Tier = tier,
            Division = division,
            LeaguePoints = leaguePoints,
            Wins = 10,
            Losses = 10,
        };

    private ApiWebApplicationFactory CreateFactory() => new(_fixture);

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(fixture);
}
