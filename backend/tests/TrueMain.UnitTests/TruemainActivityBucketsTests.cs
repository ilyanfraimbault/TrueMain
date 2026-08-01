using AwesomeAssertions;
using TrueMain.Services.Truemains;

namespace TrueMain.UnitTests;

/// <summary>
/// The activity grid's mode boundaries (#927). Everything asserted here is the
/// part of the endpoint that has no database in it: how games fold into cells,
/// where a window starts, and — the point of the feature — which cells are
/// allowed to exist at all.
/// </summary>
/// <remarks>
/// Two invariants carry most of these tests. <b>Empty is not zero</b>: a day with
/// no games has a null win rate, never <c>0.0</c>, because a 0% day is a
/// measurement and an idle day is not. <b>Erased is not idle</b>: match rows are
/// hard-deleted past the retention window, so the calendar windows stop at the
/// oldest game still on disk instead of drawing empty cells over a period nobody
/// can speak for.
/// </remarks>
public class TruemainActivityBucketsTests
{
    // A Wednesday, so week flooring has to move backwards by a non-zero, non-six
    // offset — the two values a `% 7` bug would land on.
    private static readonly DateTime Wednesday = new(2026, 7, 29, 14, 30, 0, DateTimeKind.Utc);

    private static ActivityGameRow Game(DateTime startUtc, bool win = true, int championId = 157)
        => new($"EUW1_{startUtc.Ticks}", startUtc, win, championId);

    // ─── Per-game series ───────────────────────────────────────────────────

    [Fact]
    public void ByGame_emits_one_decided_cell_per_game_oldest_first()
    {
        // Newest first, the order the query returns.
        var games = new[]
        {
            Game(Wednesday, win: false),
            Game(Wednesday.AddHours(-2), win: true),
            Game(Wednesday.AddHours(-4), win: true),
        };

        var buckets = TruemainActivityBuckets.ByGame(games, window: 10);

        buckets.Should().HaveCount(3);
        buckets.Select(bucket => bucket.StartUtc).Should().BeInAscendingOrder();
        buckets.Should().OnlyContain(bucket => bucket.Games == 1);
        // A single game is decided, so its rate is exactly 0 or 1 — the null case
        // belongs to empty calendar cells only.
        buckets.Select(bucket => bucket.WinRate).Should().Equal(1d, 1d, 0d);
        buckets.Should().OnlyContain(bucket => bucket.ChampionId == 157);
    }

    [Fact]
    public void ByGame_keeps_the_most_recent_window_not_the_first_rows()
    {
        var games = Enumerable.Range(0, 5)
            .Select(offset => Game(Wednesday.AddHours(-offset)))
            .ToList();

        var buckets = TruemainActivityBuckets.ByGame(games, window: 2);

        // The list arrives newest-first, so a naive Take() before the reverse would
        // still pass; what this pins is that the *retained* pair is the recent one.
        buckets.Should().HaveCount(2);
        buckets[^1].StartUtc.Should().Be(Wednesday);
        buckets[0].StartUtc.Should().Be(Wednesday.AddHours(-1));
    }

    [Fact]
    public void ByGame_returns_nothing_when_retention_left_no_games()
    {
        TruemainActivityBuckets.ByGame([], window: 60).Should().BeEmpty();
    }

    // ─── Per-day series ────────────────────────────────────────────────────

    [Fact]
    public void ByDay_fills_the_whole_requested_window_when_history_reaches_back_far_enough()
    {
        // One game on the first day of the window and one today: the range is fully
        // describable, so every day in between must be drawn.
        var games = new[]
        {
            Game(Wednesday),
            Game(Wednesday.AddDays(-29)),
            Game(Wednesday.AddDays(-40)),
        };

        var buckets = TruemainActivityBuckets.ByDay(games, Wednesday, windowDays: 30);

        buckets.Should().HaveCount(30);
        buckets[0].Key.Should().Be("2026-06-30");
        buckets[^1].Key.Should().Be("2026-07-29");
        // The 40-day-old game is outside the window and must not be folded into the
        // first cell.
        buckets[0].Games.Should().Be(1);
        buckets.Sum(bucket => bucket.Games).Should().Be(2);
    }

    [Fact]
    public void ByDay_stops_at_the_oldest_retained_game_instead_of_drawing_erased_days()
    {
        // Retention has left only three days of history. The remaining 27 cells of a
        // 30-day window are not "days off" — nobody can tell whether they were
        // played, so they must not be emitted at all.
        var games = new[]
        {
            Game(Wednesday),
            Game(Wednesday.AddDays(-2)),
        };

        var buckets = TruemainActivityBuckets.ByDay(games, Wednesday, windowDays: 30);

        buckets.Should().HaveCount(3);
        buckets[0].Key.Should().Be("2026-07-27");
        buckets[^1].Key.Should().Be("2026-07-29");
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

        var buckets = TruemainActivityBuckets.ByDay(games, Wednesday, windowDays: 30);

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

        var buckets = TruemainActivityBuckets.ByDay(games, Wednesday, windowDays: 30);

        buckets.Select(bucket => bucket.Key).Should().Equal("2026-07-28", "2026-07-29");
        buckets.Should().OnlyContain(bucket => bucket.Games == 1);
    }

    [Fact]
    public void ByDay_never_drops_a_game_newer_than_the_clock_reference()
    {
        // Riot supplies the timestamps, so a few seconds of skew is possible; a game
        // ahead of `nowUtc` must still get its cell rather than vanish past the end
        // of the grid.
        var games = new[] { Game(Wednesday.AddDays(1)) };

        var buckets = TruemainActivityBuckets.ByDay(games, Wednesday, windowDays: 30);

        buckets.Should().NotBeEmpty();
        buckets.Sum(bucket => bucket.Games).Should().Be(1);
        buckets[^1].Key.Should().Be("2026-07-30");
    }

    // ─── Per-week series ───────────────────────────────────────────────────

    [Fact]
    public void FloorToWeekUtc_snaps_every_weekday_to_its_monday()
    {
        var monday = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc);

        foreach (var offset in Enumerable.Range(0, 7))
        {
            TruemainActivityBuckets
                .FloorToWeekUtc(monday.AddDays(offset).AddHours(13))
                .Should().Be(monday, $"day +{offset} belongs to the week starting {monday:yyyy-MM-dd}");
        }

        // Sunday is the end of its ISO week, not the start of the next one — the
        // case a `DayOfWeek`-based offset gets wrong when it forgets that
        // DayOfWeek.Sunday is 0.
        TruemainActivityBuckets
            .FloorToWeekUtc(monday.AddDays(-1))
            .Should().Be(monday.AddDays(-7));
    }

    [Fact]
    public void ByWeek_folds_a_week_of_games_into_one_cell()
    {
        var monday = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc);
        var games = new[]
        {
            Game(monday.AddHours(10), win: true),
            Game(monday.AddDays(3), win: false),
            Game(monday.AddDays(6).AddHours(22), win: true),
        };

        var buckets = TruemainActivityBuckets.ByWeek(games, monday.AddDays(6), windowWeeks: 12);

        buckets.Should().HaveCount(1);
        buckets[0].Key.Should().Be("2026-07-27");
        buckets[0].Games.Should().Be(3);
        buckets[0].Wins.Should().Be(2);
        buckets[0].WinRate.Should().BeApproximately(2d / 3d, 1e-9);
    }

    [Fact]
    public void ByWeek_emits_an_idle_week_between_two_active_ones()
    {
        var monday = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc);
        var games = new[]
        {
            Game(monday.AddHours(4)),
            Game(monday.AddDays(-14).AddHours(4)),
        };

        var buckets = TruemainActivityBuckets.ByWeek(games, monday, windowWeeks: 12);

        buckets.Select(bucket => bucket.Key)
            .Should().Equal("2026-07-13", "2026-07-20", "2026-07-27");
        buckets[1].Games.Should().Be(0);
        buckets[1].WinRate.Should().BeNull();
    }

    [Fact]
    public void ByWeek_stops_at_the_oldest_retained_game()
    {
        var monday = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc);
        var games = new[] { Game(monday.AddHours(4)) };

        var buckets = TruemainActivityBuckets.ByWeek(games, monday, windowWeeks: 12);

        // Twelve weeks were asked for; one week is what the retained data can back.
        buckets.Should().HaveCount(1);
        buckets[0].Key.Should().Be("2026-07-27");
    }

    [Fact]
    public void Calendar_series_return_nothing_when_retention_left_no_games()
    {
        TruemainActivityBuckets.ByDay([], Wednesday, windowDays: 30).Should().BeEmpty();
        TruemainActivityBuckets.ByWeek([], Wednesday, windowWeeks: 12).Should().BeEmpty();
    }

    [Fact]
    public void Calendar_slots_are_utc_so_the_wire_carries_an_instant_not_a_local_date()
    {
        var buckets = TruemainActivityBuckets.ByDay([Game(Wednesday)], Wednesday, windowDays: 30);

        buckets[0].StartUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    // ─── Window defaults ───────────────────────────────────────────────────

    [Fact]
    public void Default_windows_stay_within_what_the_grid_can_draw()
    {
        // The grid is ten cells wide in the profile's left rail, so the per-game
        // window has to stay a whole number of rows; the calendar windows are the
        // spans the issue asked for.
        (TruemainActivityBuckets.GameWindow % 10).Should().Be(0);
        TruemainActivityBuckets.DayWindowDays.Should().Be(30);
        TruemainActivityBuckets.WeekWindowWeeks.Should().Be(12);
    }
}
