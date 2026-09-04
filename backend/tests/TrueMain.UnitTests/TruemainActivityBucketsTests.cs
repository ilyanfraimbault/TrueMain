using AwesomeAssertions;
using TrueMain.Services.Truemains;

namespace TrueMain.UnitTests;

/// <summary>
/// The activity grid's bucketing (#927, reshaped in #1473). Everything asserted
/// here is the part of the endpoint that has no database in it: how games fold
/// into cells, and — the point of the feature — which cells are allowed to exist
/// at all.
/// </summary>
/// <remarks>
/// Two invariants carry most of these tests. <b>Every day of the window is
/// drawn</b>: the calendar windows are given their bounds from outside (the
/// patch's measured span, or the last seven days) and emit a cell for each day in
/// between, so a player who sat out the start of a patch sees those days rather
/// than a grid that begins at their first game. <b>Empty is not zero</b>: a day
/// with no games has a null win rate, never <c>0.0</c>, because a 0% day is a
/// measurement and an idle day is not.
/// </remarks>
public class TruemainActivityBucketsTests
{
    private static readonly DateTime Wednesday = new(2026, 7, 29, 14, 30, 0, DateTimeKind.Utc);

    private static DateTime Day(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static ActivityGameRow Game(DateTime startUtc, bool win = true, int championId = 157)
        => new($"EUW1_{startUtc.Ticks}", startUtc, win, championId);

    // ─── Calendar window ───────────────────────────────────────────────────

    [Fact]
    public void ByDay_draws_every_day_of_the_window_including_the_ones_before_the_first_game()
    {
        // The player's first game of the window is on its fourth day. The three days
        // before it are days of the patch they sat out, and the whole reason the
        // window is measured globally is so those get drawn.
        var games = new[] { Game(Day(2026, 7, 4).AddHours(20)) };

        var buckets = TruemainActivityBuckets.ByDay(games, Day(2026, 7, 1), Day(2026, 7, 10));

        buckets.Should().HaveCount(10);
        buckets[0].Key.Should().Be("2026-07-01");
        buckets[^1].Key.Should().Be("2026-07-10");
        buckets.Take(3).Should().OnlyContain(bucket => bucket.Games == 0);
        buckets[3].Games.Should().Be(1);
    }

    [Fact]
    public void ByDay_ignores_games_outside_the_window()
    {
        var games = new[]
        {
            Game(Day(2026, 7, 5).AddHours(2)),
            Game(Day(2026, 6, 30).AddHours(2)),
            Game(Day(2026, 7, 11).AddHours(2)),
        };

        var buckets = TruemainActivityBuckets.ByDay(games, Day(2026, 7, 1), Day(2026, 7, 10));

        buckets.Sum(bucket => bucket.Games).Should().Be(1);
        buckets.Single(bucket => bucket.Games > 0).Key.Should().Be("2026-07-05");
    }

    [Fact]
    public void ByDay_marks_an_idle_day_as_empty_and_a_lost_day_as_zero()
    {
        var games = new[]
        {
            Game(Wednesday, win: false),
            Game(Wednesday.AddHours(-1), win: false),
            Game(Wednesday.AddDays(-2), win: true),
        };

        var buckets = TruemainActivityBuckets.ByDay(
            games,
            TruemainActivityBuckets.FloorToDayUtc(Wednesday.AddDays(-2)),
            TruemainActivityBuckets.FloorToDayUtc(Wednesday));

        buckets.Should().HaveCount(3);

        // Day -2: played and won.
        buckets[0].Games.Should().Be(1);
        buckets[0].WinRate.Should().Be(1d);

        // Day -1: did not play. Null win rate — this is the distinction the whole
        // feature turns on.
        buckets[1].Games.Should().Be(0);
        buckets[1].Wins.Should().Be(0);
        buckets[1].WinRate.Should().BeNull();

        // Day 0: played two, lost both. A real 0%, and it must not look like the
        // idle day above.
        buckets[2].Games.Should().Be(2);
        buckets[2].WinRate.Should().Be(0d);
    }

    [Fact]
    public void ByDay_buckets_on_the_utc_day_not_on_the_elapsed_24_hours()
    {
        // 23:50 and 00:10 are 20 minutes apart but belong to different UTC days.
        var games = new[]
        {
            Game(new DateTime(2026, 7, 29, 0, 10, 0, DateTimeKind.Utc)),
            Game(new DateTime(2026, 7, 28, 23, 50, 0, DateTimeKind.Utc)),
        };

        var buckets = TruemainActivityBuckets.ByDay(games, Day(2026, 7, 28), Day(2026, 7, 29));

        buckets.Select(bucket => bucket.Key).Should().Equal("2026-07-28", "2026-07-29");
        buckets.Should().OnlyContain(bucket => bucket.Games == 1);
    }

    [Fact]
    public void ByDay_draws_an_untouched_window_rather_than_nothing()
    {
        // A patch the player has not queued a single game on is still a patch, and
        // its days are still days. An empty grid of real days is the answer; an
        // empty series would be a different (and wrong) claim.
        var buckets = TruemainActivityBuckets.ByDay([], Day(2026, 7, 1), Day(2026, 7, 7));

        buckets.Should().HaveCount(7);
        buckets.Should().OnlyContain(bucket => bucket.Games == 0 && bucket.WinRate == null);
    }

    [Fact]
    public void ByDay_returns_nothing_when_the_window_is_inverted()
    {
        TruemainActivityBuckets.ByDay([Game(Wednesday)], Day(2026, 7, 10), Day(2026, 7, 1)).Should().BeEmpty();
    }

    [Fact]
    public void ByDay_cells_are_utc_so_the_wire_carries_an_instant_not_a_local_date()
    {
        var buckets = TruemainActivityBuckets.ByDay([Game(Wednesday)], Day(2026, 7, 29), Day(2026, 7, 29));

        buckets[0].StartUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    // ─── Day window (one cell per game) ────────────────────────────────────

    [Fact]
    public void ByGame_emits_one_decided_cell_per_game_of_that_day_oldest_first()
    {
        // Newest first, the order the query returns.
        var games = new[]
        {
            Game(Wednesday, win: false),
            Game(Wednesday.AddHours(-2), win: true),
            Game(Wednesday.AddHours(-4), win: true),
        };

        var buckets = TruemainActivityBuckets.ByGame(games, Wednesday);

        buckets.Should().HaveCount(3);
        buckets.Select(bucket => bucket.StartUtc).Should().BeInAscendingOrder();
        buckets.Should().OnlyContain(bucket => bucket.Games == 1);
        // A single game is decided, so its rate is exactly 0 or 1 — the null case
        // belongs to empty calendar cells only.
        buckets.Select(bucket => bucket.WinRate).Should().Equal(1d, 1d, 0d);
        buckets.Should().OnlyContain(bucket => bucket.ChampionId == 157);
    }

    [Fact]
    public void ByGame_keeps_only_the_games_of_the_requested_utc_day()
    {
        var games = new[]
        {
            Game(Wednesday),
            // 40 minutes earlier, and on the previous UTC day.
            Game(new DateTime(2026, 7, 28, 23, 50, 0, DateTimeKind.Utc)),
            Game(new DateTime(2026, 7, 30, 0, 10, 0, DateTimeKind.Utc)),
        };

        var buckets = TruemainActivityBuckets.ByGame(games, Wednesday);

        buckets.Should().ContainSingle();
        buckets[0].StartUtc.Should().Be(Wednesday);
    }

    [Fact]
    public void ByGame_returns_nothing_on_a_rest_day()
    {
        TruemainActivityBuckets.ByGame([Game(Wednesday.AddDays(-1))], Wednesday).Should().BeEmpty();
        TruemainActivityBuckets.ByGame([], Wednesday).Should().BeEmpty();
    }

    // ─── Window defaults ───────────────────────────────────────────────────

    [Fact]
    public void The_week_window_is_a_week()
    {
        TruemainActivityBuckets.WeekWindowDays.Should().Be(7);
    }
}
