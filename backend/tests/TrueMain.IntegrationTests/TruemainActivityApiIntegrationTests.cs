using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Core.Lol.Ranking;
using Data;
using Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.IntegrationTests;

/// <summary>
/// End-to-end cover for the profile activity grid (#927). The bucketing maths is
/// unit-tested (<c>TruemainActivityBucketsTests</c>); what needs a real Postgres
/// is the pair of reads behind it — the participant join that feeds the
/// game / day / week series and the aggregate group-by that feeds the patch one —
/// and, above all, that those two sources stay wired to the right questions.
/// </summary>
/// <remarks>
/// The suite is deliberately built around the retention asymmetry, because that is
/// the whole reason the endpoint has four modes instead of one. Two facts are
/// asserted repeatedly: match-sourced series see only what is still on disk, and
/// the patch series equals the dedication card's own numbers to the game.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class TruemainActivityApiIntegrationTests
{
    private const int RankedQueueId = 420;
    private const int Yasuo = 157;
    private const int Ahri = 103;

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
    public async Task Folds_the_same_games_three_ways_and_never_zeroes_an_idle_day()
    {
        await _fixture.ResetDatabaseAsync();
        var midday = Midday;

        var account = Account("grinder-puuid", "Grinder");
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(account);

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

        // Every match-sourced series counts the same three games — that identity is
        // the reason all four ship in one response.
        activity!.Game.Games.Should().Be(3);
        activity.Day.Games.Should().Be(3);
        activity.Week.Games.Should().Be(3);
        activity.Game.Wins.Should().Be(1);
        activity.Day.Wins.Should().Be(1);

        activity.Game.Source.Should().Be(TruemainActivityKinds.MatchesSource);
        activity.Game.Scope.Should().Be(TruemainActivityKinds.AllChampionsScope);
        activity.Game.RetentionBounded.Should().BeTrue();

        // One cell per game, each decided.
        activity.Game.Buckets.Should().HaveCount(3);
        activity.Game.Buckets.Should().OnlyContain(bucket => bucket.Games == 1);
        activity.Game.Buckets.Should().OnlyContain(bucket => bucket.WinRate == 0d || bucket.WinRate == 1d);
        activity.Game.Buckets.Should().OnlyContain(bucket => bucket.ChampionId == Yasuo);

        // The day series spans oldest game → today, so exactly three cells; the
        // middle one is empty and its win rate must be null rather than 0.
        activity.Day.Buckets.Should().HaveCount(3);
        var idle = activity.Day.Buckets[1];
        idle.Games.Should().Be(0);
        idle.WinRate.Should().BeNull("an idle day is not a 0% day");

        var lostDay = activity.Day.Buckets[2];
        lostDay.Games.Should().Be(2);
        lostDay.WinRate.Should().Be(0d, "two losses is a measured 0%, unlike the idle day above");

        // Coverage is reported so the UI can say what it is showing.
        activity.Day.CoverageFromUtc.Should().NotBeNull();
        activity.Day.CoverageToUtc.Should().NotBeNull();
        activity.Day.CoverageFromUtc.Should().BeOnOrBefore(activity.Day.CoverageToUtc!.Value);
    }

    [Fact]
    public async Task Counts_only_the_tracked_ranked_queue_so_the_modes_share_one_population()
    {
        await _fixture.ResetDatabaseAsync();
        var midday = Midday;

        var account = Account("flex-puuid", "FlexPlayer");
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(account);
            AddGame(db, account, "EUW1_SOLO", midday.AddHours(-1), win: true);
            // ARAM: stored history exists, but every aggregate — and therefore the
            // patch series — is hard-scoped to ranked solo. Counting it here would
            // make two modes of one grid disagree about the same afternoon.
            AddGame(db, account, "EUW1_ARAM", midday, win: false, queueId: 450);
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var activity = await client.GetFromJsonAsync<TruemainActivityReadModel>(
            "/truemains/FlexPlayer-EUW1/activity");

        activity!.Game.Games.Should().Be(1);
        activity.Game.Buckets.Should().ContainSingle()
            .Which.Key.Should().Be("EUW1_SOLO");
    }

    [Fact]
    public async Task Reports_an_emptied_retention_window_as_empty_rather_than_as_an_idle_month()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        // A player whose matches retention has already pruned, but whose frozen
        // aggregates survive — the exact asymmetry the four modes exist for.
        var account = Account("retired-puuid", "Retired");
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(account);
            db.RankSnapshots.Add(Snapshot(account, now));
            db.MainChampionStats.Add(MainStat(account, Yasuo, playRate: 0.8d, now));
            db.ChampionAggregateScopes.AddRange(
                Scope(account.Id, Yasuo, "15.1", games: 30, wins: 18, now.AddDays(-120)),
                Scope(account.Id, Yasuo, "15.2", games: 20, wins: 9, now.AddDays(-95)));
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var activity = await client.GetFromJsonAsync<TruemainActivityReadModel>(
            "/truemains/Retired-EUW1/activity");

        // No match rows left: no cells at all, and a null win rate rather than 0%.
        // Drawing 30 empty day cells would claim the player was idle, when in fact
        // the games were deleted.
        activity!.Day.Buckets.Should().BeEmpty();
        activity.Day.Games.Should().Be(0);
        activity.Day.WinRate.Should().BeNull();
        activity.Day.CoverageFromUtc.Should().BeNull();
        activity.Game.Buckets.Should().BeEmpty();
        activity.Week.Buckets.Should().BeEmpty();

        // The patch series still holds the whole career — the one mode that can.
        activity.Patch.Source.Should().Be(TruemainActivityKinds.AggregatesSource);
        activity.Patch.RetentionBounded.Should().BeFalse();
        activity.Patch.Buckets.Should().HaveCount(2);
        activity.Patch.Games.Should().Be(50);
        activity.Patch.Wins.Should().Be(27);
    }

    /// <summary>
    /// The issue's acceptance criterion: patch mode must match the aggregate
    /// numbers shown elsewhere on the page. "Elsewhere" is the dedication card,
    /// which sums the very same scope rows — so the two are asserted against each
    /// other in one request pair rather than against a hardcoded expectation.
    /// </summary>
    [Fact]
    public async Task Patch_series_equals_the_dedication_card_it_sits_under()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        var account = Account("devoted-puuid", "Devoted");
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(account);
            db.RankSnapshots.Add(Snapshot(account, now));

            // Yasuo is the signature champion; Ahri is a lesser main whose games
            // must not leak into the Yasuo grid.
            db.MainChampionStats.Add(MainStat(account, Yasuo, playRate: 0.7d, now));
            db.MainChampionStats.Add(MainStat(account, Ahri, playRate: 0.2d, now));

            db.ChampionAggregateScopes.AddRange(
                // Two scope rows on the same patch (different lanes) must fold into
                // one cell — the grid is per patch, not per scope row.
                Scope(account.Id, Yasuo, "15.1", games: 20, wins: 11, now.AddDays(-40), position: "MIDDLE"),
                Scope(account.Id, Yasuo, "15.1", games: 5, wins: 2, now.AddDays(-38), position: "TOP"),
                Scope(account.Id, Yasuo, "15.2", games: 25, wins: 13, now.AddDays(-10)),
                Scope(account.Id, Yasuo, "15.10", games: 15, wins: 4, now.AddDays(-2)),
                Scope(account.Id, Ahri, "15.2", games: 40, wins: 30, now.AddDays(-9)));

            // A different queue on the signature champion: out of scope for both
            // the dedication card and this grid.
            var otherQueue = Scope(account.Id, Yasuo, "15.2", games: 500, wins: 400, now);
            otherQueue.QueueId = 400;
            db.ChampionAggregateScopes.Add(otherQueue);

            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var activity = await client.GetFromJsonAsync<TruemainActivityReadModel>(
            "/truemains/Devoted-EUW1/activity");
        var profile = await client.GetFromJsonAsync<ProfileReadModel>(
            "/truemains/Devoted-EUW1/profile");

        var patch = activity!.Patch;
        var dedication = profile!.Dedication;
        dedication.Should().NotBeNull();

        patch.Scope.Should().Be(TruemainActivityKinds.ChampionScope);
        patch.ChampionId.Should().Be(dedication!.ChampionId, "both surfaces score the same signature champion");
        patch.ChampionId.Should().Be(Yasuo);

        // The two invariants a reader can check by eye on the page.
        patch.Games.Should().Be(dedication.CareerGames);
        patch.Buckets.Should().HaveCount(dedication.PatchSpan);

        patch.Games.Should().Be(65, "20 + 5 + 25 + 15 ranked Yasuo games, Ahri and queue 400 excluded");
        patch.Wins.Should().Be(30);

        // Patches sort by their numeric key, not as text — "15.10" is newer than
        // "15.2", which a string sort gets backwards.
        patch.Buckets.Select(bucket => bucket.Key).Should().Equal("15.1", "15.2", "15.10");

        var firstPatch = patch.Buckets[0];
        firstPatch.Games.Should().Be(25, "the two lane scopes on 15.1 fold into one patch cell");
        firstPatch.Wins.Should().Be(13);
        firstPatch.StartUtc.Should().BeNull("a patch has no stored start instant");
    }

    [Fact]
    public async Task Leaves_the_patch_series_empty_when_no_champion_is_classified_as_a_main()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        // Aggregates exist but no main is classified, so there is nothing to scope
        // a patch history to. Widening the series to every champion would answer a
        // different question under the same heading.
        var account = Account("unclassified-puuid", "Unclassified");
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(account);
            db.ChampionAggregateScopes.Add(
                Scope(account.Id, Yasuo, "15.2", games: 12, wins: 6, now.AddDays(-4)));
            AddGame(db, account, "EUW1_ONE", Midday, win: true);
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var activity = await client.GetFromJsonAsync<TruemainActivityReadModel>(
            "/truemains/Unclassified-EUW1/activity");

        activity!.Patch.ChampionId.Should().BeNull();
        activity.Patch.Buckets.Should().BeEmpty();
        activity.Patch.Games.Should().Be(0);
        activity.Patch.WinRate.Should().BeNull();

        // The match-sourced series are unaffected — the account is real and played.
        activity.Game.Games.Should().Be(1);
    }

    /// <summary>
    /// Midday UTC of the current day — the anchor every seeded game hangs off.
    /// </summary>
    /// <remarks>
    /// Timestamps must never be built from a bare <c>DateTime.UtcNow</c> offset
    /// here: the day and week series bucket on the UTC calendar, so a suite run at
    /// 00:20 UTC would see <c>now.AddHours(-3)</c> land on the *previous* day and
    /// the cell counts asserted below would change with the wall clock. Anchoring on
    /// midday keeps ±11 hours of headroom in both directions, so the same
    /// assertions hold whenever CI happens to run.
    /// </remarks>
    private static DateTime Midday
        => DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc).AddHours(12);

    private static void AddGame(
        TrueMainDbContext db,
        RiotAccount account,
        string matchId,
        DateTime startUtc,
        bool win,
        int queueId = RankedQueueId,
        int championId = Yasuo)
        => MatchParticipantSeed.AddMatchWithParticipant(
            db,
            matchId,
            account.PlatformId,
            queueId,
            startUtc,
            account.Puuid,
            championId,
            win,
            account.Id);

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

    private static RankSnapshot Snapshot(RiotAccount account, DateTime now)
    {
        account.Score = RankScore.Compute("DIAMOND", "I", 40);
        return new RankSnapshot
        {
            Id = Guid.NewGuid(),
            RiotAccount = account,
            CapturedAtUtc = now,
            Tier = "DIAMOND",
            Division = "I",
            LeaguePoints = 40,
            Wins = 50,
            Losses = 50,
        };
    }

    private static MainChampionStat MainStat(
        RiotAccount account,
        int championId,
        double playRate,
        DateTime now)
        => new()
        {
            Id = Guid.NewGuid(),
            PlatformId = account.PlatformId,
            Puuid = account.Puuid,
            ChampionId = championId,
            TotalMatches = 50,
            ChampionMatches = (int)Math.Round(50 * playRate),
            PlayRate = playRate,
            IsMain = true,
            IsOtp = playRate >= 0.85d,
            PrimaryPosition = "MIDDLE",
            PositionBreakdown = [new PositionStat { Position = "MIDDLE", Games = 50, Rate = 1d }],
            CalculatedAtUtc = now,
        };

    private static ChampionAggregateScope Scope(
        Guid riotAccountId,
        int championId,
        string patch,
        int games,
        int wins,
        DateTime lastGameUtc,
        string position = "MIDDLE")
        => new()
        {
            Id = Guid.NewGuid(),
            RiotAccountId = riotAccountId,
            ChampionId = championId,
            GameVersion = patch,
            PlatformId = "EUW1",
            QueueId = RankedQueueId,
            Position = position,
            EloBracket = EloBracket.Diamond,
            // Mains: the population these fixtures have always described; a
            // non-nullable bool is always written, so the column default never
            // applies and an unset flag would seed a non-main (#1346).
            IsMain = true,
            Games = games,
            Wins = wins,
            Kills = games,
            Deaths = games,
            Assists = games,
            LastGameStartTimeUtc = lastGameUtc,
            AggregatedAtUtc = lastGameUtc,
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
