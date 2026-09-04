using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data;
using Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.IntegrationTests;

/// <summary>
/// End-to-end cover for the profile activity grid (#927, reshaped in #1473). The
/// bucketing maths is unit-tested (<c>TruemainActivityBucketsTests</c>); what
/// needs a real Postgres is the pair of reads behind it — the participant join
/// that feeds every window, and the global group-by that measures the current
/// patch's span.
/// </summary>
/// <remarks>
/// The suite is built around the one thing the maths cannot decide on its own:
/// <b>where the patch window starts</b>. It is measured over every player's
/// matches, not this player's, so a day the profile's owner sat out at the start
/// of the patch is still drawn as an idle day — which is the whole point of the
/// window and the thing a per-player bound would silently get wrong.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class TruemainActivityApiIntegrationTests
{
    private const int RankedQueueId = 420;
    private const int Yasuo = 157;

    /// <summary>The patch every fixture plays on unless it says otherwise.</summary>
    private const string CurrentPatch = "16.6";

    private const string CurrentPatchVersion = "16.6.1";
    private const string PreviousPatchVersion = "16.5.1";

    private readonly PostgresFixture _fixture;

    public TruemainActivityApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Returns_404_for_an_unknown_or_malformed_name_tag()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        (await client.GetAsync("/truemains/Unknown-NA1/activity")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/truemains/NoHyphen/activity")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Draws_every_day_of_the_patch_including_the_ones_before_the_player_joined_it()
    {
        await _fixture.ResetDatabaseAsync();
        var midday = Midday;

        var account = Account("grinder-puuid", "Grinder");
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(account);

            // Somebody else's game opens the patch six days ago. Our player only
            // shows up on day -2, so days -6 .. -3 are days of the patch they sat
            // out — and they have to be drawn.
            var stranger = Account("stranger-puuid", "Stranger");
            db.RiotAccounts.Add(stranger);
            AddGame(db, stranger, "EUW1_OPENER", midday.AddDays(-6), win: true);

            // Today: two games, both lost — a real 0%, which must not look like an
            // absence. Two days ago: one win. Yesterday: nothing at all.
            AddGame(db, account, "EUW1_1", midday.AddHours(-1), win: false);
            AddGame(db, account, "EUW1_2", midday, win: false);
            AddGame(db, account, "EUW1_3", midday.AddDays(-2), win: true);

            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var activity = await client.GetFromJsonAsync<TruemainActivityReadModel>(
            "/truemains/Grinder-EUW1/activity");

        activity.Should().NotBeNull();

        var patch = activity!.Patch;
        patch.Mode.Should().Be(TruemainActivityKinds.PatchMode);
        patch.Patch.Should().Be(CurrentPatch);

        // Seven cells: the patch opened six days ago and today is its last day.
        patch.Buckets.Should().HaveCount(7);
        patch.Buckets[0].Key.Should().Be(DayKey(midday.AddDays(-6)));
        patch.Buckets[^1].Key.Should().Be(DayKey(midday));

        // The stranger's game opened the window but is not counted in it.
        patch.Games.Should().Be(3);
        patch.Wins.Should().Be(1);

        // Days -6 .. -3: patch days the player sat out. Idle, not erased and not 0%.
        patch.Buckets.Take(4).Should().OnlyContain(bucket => bucket.Games == 0 && bucket.WinRate == null);

        patch.Buckets[4].Games.Should().Be(1);
        patch.Buckets[4].WinRate.Should().Be(1d);

        patch.Buckets[5].Games.Should().Be(0);
        patch.Buckets[5].WinRate.Should().BeNull("an idle day is not a 0% day");

        patch.Buckets[6].Games.Should().Be(2);
        patch.Buckets[6].WinRate.Should().Be(0d, "two losses is a measured 0%, unlike the idle day above");

        // Coverage is read off the cells, so it cannot disagree with them.
        patch.CoverageFromUtc.Should().Be(activity.Patch.Buckets[0].StartUtc);
        patch.CoverageToUtc.Should().Be(activity.Patch.Buckets[^1].StartUtc);
    }

    [Fact]
    public async Task Folds_the_same_games_three_ways_over_three_windows_of_one_unit()
    {
        await _fixture.ResetDatabaseAsync();
        var midday = Midday;

        var account = Account("threeways-puuid", "ThreeWays");
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(account);
            AddGame(db, account, "EUW1_TODAY_1", midday.AddHours(-2), win: true);
            AddGame(db, account, "EUW1_TODAY_2", midday, win: false);
            AddGame(db, account, "EUW1_OLD", midday.AddDays(-3), win: true);
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var activity = await client.GetFromJsonAsync<TruemainActivityReadModel>(
            "/truemains/ThreeWays-EUW1/activity");

        // The week window is always a week — seven days, today last.
        activity!.Week.Buckets.Should().HaveCount(7);
        activity.Week.Buckets[^1].Key.Should().Be(DayKey(midday));
        activity.Week.Buckets[0].Key.Should().Be(DayKey(midday.AddDays(-6)));
        activity.Week.Games.Should().Be(3);
        activity.Week.Wins.Should().Be(2);

        // The day window is the one place a cell is a game rather than a day: two
        // games today, oldest first, each decided.
        activity.Day.Buckets.Should().HaveCount(2);
        activity.Day.Buckets.Select(bucket => bucket.Key).Should().Equal("EUW1_TODAY_1", "EUW1_TODAY_2");
        activity.Day.Buckets.Should().OnlyContain(bucket => bucket.Games == 1);
        activity.Day.Buckets.Select(bucket => bucket.WinRate).Should().Equal(1d, 0d);
        activity.Day.Buckets.Should().OnlyContain(bucket => bucket.ChampionId == Yasuo);

        // The patch and the week fold the same three games; the day only today's.
        activity.Patch.Games.Should().Be(3);
        activity.Day.Games.Should().Be(2);
    }

    [Fact]
    public async Task Leaves_the_day_window_empty_on_a_rest_day_without_emptying_the_others()
    {
        await _fixture.ResetDatabaseAsync();
        var midday = Midday;

        var account = Account("resting-puuid", "Resting");
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(account);
            AddGame(db, account, "EUW1_YESTERDAY", midday.AddDays(-1), win: true);
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var activity = await client.GetFromJsonAsync<TruemainActivityReadModel>(
            "/truemains/Resting-EUW1/activity");

        // No games today: nothing to draw, and a null rate rather than 0%. There is
        // no such thing as an "idle game", so the window is genuinely empty.
        activity!.Day.Buckets.Should().BeEmpty();
        activity.Day.Games.Should().Be(0);
        activity.Day.WinRate.Should().BeNull();
        activity.Day.CoverageFromUtc.Should().BeNull();

        // The calendar windows still draw their days, one of which is played.
        activity.Week.Buckets.Should().HaveCount(7);
        activity.Week.Games.Should().Be(1);
        activity.Patch.Games.Should().Be(1);
    }

    [Fact]
    public async Task Draws_the_patch_for_a_player_who_has_not_queued_a_single_game_on_it()
    {
        await _fixture.ResetDatabaseAsync();
        var midday = Midday;

        var account = Account("absent-puuid", "Absent");
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(account);
            // The patch exists — other people are playing it — and the player's own
            // games are all on the previous one.
            var stranger = Account("stranger-puuid", "Stranger");
            db.RiotAccounts.Add(stranger);
            AddGame(db, stranger, "EUW1_OPENER", midday.AddDays(-3), win: true);
            AddGame(db, account, "EUW1_LAST_PATCH", midday.AddDays(-9), win: true, gameVersion: PreviousPatchVersion);
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var activity = await client.GetFromJsonAsync<TruemainActivityReadModel>(
            "/truemains/Absent-EUW1/activity");

        // Four real days, every one of them idle. An empty series would be a
        // different claim ("there is no patch to show"); a grid of idle days is the
        // true one.
        activity!.Patch.Patch.Should().Be(CurrentPatch);
        activity.Patch.Buckets.Should().HaveCount(4);
        activity.Patch.Buckets.Should().OnlyContain(bucket => bucket.Games == 0 && bucket.WinRate == null);
        activity.Patch.Games.Should().Be(0);
        activity.Patch.WinRate.Should().BeNull();

        // Their one game is on the previous patch, nine days back: outside the patch
        // window and outside the week too.
        activity.Week.Games.Should().Be(0);
    }

    [Fact]
    public async Task Measures_the_window_on_the_current_patch_not_on_the_whole_retained_history()
    {
        await _fixture.ResetDatabaseAsync();
        var midday = Midday;

        var account = Account("twopatch-puuid", "TwoPatch");
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(account);
            // Two patches on disk. The current one is the one whose first game is the
            // most recent, and only its days may be drawn.
            AddGame(db, account, "EUW1_OLD_PATCH", midday.AddDays(-5), win: true, gameVersion: PreviousPatchVersion);
            AddGame(db, account, "EUW1_NEW_PATCH", midday.AddDays(-2), win: false);
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var activity = await client.GetFromJsonAsync<TruemainActivityReadModel>(
            "/truemains/TwoPatch-EUW1/activity");

        activity!.Patch.Patch.Should().Be(CurrentPatch);
        activity.Patch.Buckets.Should().HaveCount(3);
        activity.Patch.Buckets[0].Key.Should().Be(DayKey(midday.AddDays(-2)));
        activity.Patch.Games.Should().Be(1, "the previous patch's game is outside the window");

        // The week window is a calendar span, not a patch one, so it does see both.
        activity.Week.Games.Should().Be(2);
    }

    [Fact]
    public async Task Counts_only_the_tracked_ranked_queue_so_the_windows_share_one_population()
    {
        await _fixture.ResetDatabaseAsync();
        var midday = Midday;

        var account = Account("flex-puuid", "FlexPlayer");
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(account);
            AddGame(db, account, "EUW1_SOLO", midday.AddHours(-1), win: true);
            // ARAM: stored history exists, but the profile counts ranked solo
            // everywhere else, and a grid that disagreed with the summary above it
            // would be exactly the failure this endpoint is shaped to avoid.
            AddGame(db, account, "EUW1_ARAM", midday, win: false, queueId: 450);
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var activity = await client.GetFromJsonAsync<TruemainActivityReadModel>(
            "/truemains/FlexPlayer-EUW1/activity");

        activity!.Day.Games.Should().Be(1);
        activity.Day.Buckets.Should().ContainSingle()
            .Which.Key.Should().Be("EUW1_SOLO");
        activity.Patch.Games.Should().Be(1);
    }

    [Fact]
    public async Task Reports_an_empty_patch_series_when_no_tracked_match_carries_a_patch()
    {
        await _fixture.ResetDatabaseAsync();

        // A known account and an empty match table: there is no patch to measure,
        // and inventing a window from nothing would be a fabricated claim.
        var account = Account("fresh-puuid", "Fresh");
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(account);
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var activity = await client.GetFromJsonAsync<TruemainActivityReadModel>(
            "/truemains/Fresh-EUW1/activity");

        activity!.Patch.Patch.Should().BeNull();
        activity.Patch.Buckets.Should().BeEmpty();
        activity.Patch.WinRate.Should().BeNull();

        // The week window does not depend on a patch, so it still draws its days.
        activity.Week.Buckets.Should().HaveCount(7);
        activity.Week.Games.Should().Be(0);
        activity.Day.Buckets.Should().BeEmpty();
    }

    /// <summary>
    /// Midday UTC of the current day — the anchor every seeded game hangs off.
    /// </summary>
    /// <remarks>
    /// Timestamps must never be built from a bare <c>DateTime.UtcNow</c> offset
    /// here: every window buckets on the UTC calendar, so a suite run at 00:20 UTC
    /// would see <c>now.AddHours(-3)</c> land on the *previous* day and the cell
    /// counts asserted below would change with the wall clock. Anchoring on midday
    /// keeps ±11 hours of headroom in both directions, so the same assertions hold
    /// whenever CI happens to run.
    /// </remarks>
    private static DateTime Midday
        => DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc).AddHours(12);

    private static string DayKey(DateTime instant)
        => instant.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static void AddGame(
        TrueMainDbContext db,
        RiotAccount account,
        string matchId,
        DateTime startUtc,
        bool win,
        int queueId = RankedQueueId,
        int championId = Yasuo,
        string gameVersion = CurrentPatchVersion)
        => MatchParticipantSeed.AddMatchWithParticipant(
            db,
            matchId,
            account.PlatformId,
            queueId,
            startUtc,
            account.Puuid,
            championId,
            win,
            account.Id,
            gameVersion: gameVersion);

    private static RiotAccount Account(string puuid, string gameName)
        => new()
        {
            Id = Guid.NewGuid(),
            Puuid = puuid,
            GameName = gameName,
            TagLine = "EUW1",
            PlatformId = "EUW1",
            ProfileIconId = 1,
            SummonerLevel = 100,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            LastMatchIngestAtUtc = DateTime.UtcNow,
        };

    private ApiWebApplicationFactory CreateFactory() => new(_fixture);

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(
            fixture, [new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420")]);
}
