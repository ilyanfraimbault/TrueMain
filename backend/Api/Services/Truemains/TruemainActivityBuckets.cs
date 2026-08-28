using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

/// <summary>
/// One game of a player's retained history, as the activity read needs it.
/// </summary>
/// <param name="MatchId">Riot match id — the game series' cell key.</param>
/// <param name="StartUtc">Game start; the only thing the calendar foldings bucket on.</param>
/// <param name="Win">Whether the player won.</param>
/// <param name="ChampionId">Champion the player was on.</param>
internal sealed record ActivityGameRow(string MatchId, DateTime StartUtc, bool Win, int ChampionId);

/// <summary>
/// The activity grid's bucketing — pure, so the mode boundaries can be tested
/// without a database. Every folding here reads the same game list, which is what
/// makes the three match-sourced modes structurally incapable of disagreeing
/// about the same afternoon.
/// </summary>
/// <remarks>
/// <para>
/// Two rules run through all of it.
/// </para>
/// <para>
/// <b>An empty period and a lost period are different facts.</b> A calendar cell
/// with no games carries <c>Games = 0</c> and a <see langword="null"/> win rate;
/// only a cell with games can carry <c>0.0</c>. The read models never fill a gap
/// with a zero.
/// </para>
/// <para>
/// <b>An erased period is not an idle one.</b> <c>match_participants</c> is
/// hard-deleted past <c>MatchDataRetention:RetainedPatchCount</c> patches, so
/// before the oldest game still on disk we cannot tell "did not play" from "no
/// longer stored". The calendar windows are therefore clamped to that oldest
/// game and the earlier cells are not emitted at all — the caller reports the
/// clamped range as the series' coverage and the UI states it. Rendering them as
/// empty cells would be a fabricated "you were idle".
/// </para>
/// </remarks>
internal static class TruemainActivityBuckets
{
    /// <summary>
    /// Cells in the per-game series. 60 is six rows of ten in the profile's
    /// left rail, and comfortably inside what retention holds for an active
    /// player — the window is a grid-shape choice, not a data bound.
    /// </summary>
    public const int GameWindow = 60;

    /// <summary>Cells in the per-day series — the ~month the issue asked for.</summary>
    public const int DayWindowDays = 30;

    /// <summary>
    /// Cells in the per-week series. A quarter is the widest span that is worth
    /// asking for; retention will usually clamp it to far fewer, which is the
    /// honest answer rather than a shortfall.
    /// </summary>
    public const int WeekWindowWeeks = 12;

    /// <summary>
    /// One cell per game, oldest first.
    /// </summary>
    /// <param name="gamesNewestFirst">
    /// The player's retained games, newest first (the query orders that way so its
    /// row cap keeps the recent end of the history).
    /// </param>
    /// <param name="window">How many of the most recent games to keep.</param>
    public static IReadOnlyList<TruemainActivityBucketReadModel> ByGame(
        IReadOnlyList<ActivityGameRow> gamesNewestFirst,
        int window)
    {
        var buckets = new List<TruemainActivityBucketReadModel>(Math.Min(window, gamesNewestFirst.Count));

        // Walk the newest `window` games backwards so the output reads oldest →
        // newest like both calendar series, and the client never has to know that
        // one mode arrives in the opposite order.
        var count = Math.Min(window, gamesNewestFirst.Count);
        for (var i = count - 1; i >= 0; i--)
        {
            var game = gamesNewestFirst[i];
            buckets.Add(new TruemainActivityBucketReadModel
            {
                Key = game.MatchId,
                StartUtc = game.StartUtc,
                Games = 1,
                Wins = game.Win ? 1 : 0,
                // A single game is a decided fact, so the rate is exactly 0 or 1
                // — never null. The null case only exists for empty calendar
                // cells, which this series cannot produce.
                WinRate = game.Win ? 1d : 0d,
                ChampionId = game.ChampionId,
            });
        }

        return buckets;
    }

    /// <summary>
    /// One cell per UTC calendar day over the last <paramref name="windowDays"/>
    /// days, clamped to the oldest retained game. Empty days inside the range are
    /// emitted; days before it are not.
    /// </summary>
    public static IReadOnlyList<TruemainActivityBucketReadModel> ByDay(
        IReadOnlyList<ActivityGameRow> games,
        DateTime nowUtc,
        int windowDays)
        => Calendar(games, nowUtc, windowDays, 1, FloorToDayUtc);

    /// <summary>
    /// One cell per ISO week (Monday 00:00 UTC) over the last
    /// <paramref name="windowWeeks"/> weeks, clamped to the oldest retained game.
    /// </summary>
    public static IReadOnlyList<TruemainActivityBucketReadModel> ByWeek(
        IReadOnlyList<ActivityGameRow> games,
        DateTime nowUtc,
        int windowWeeks)
        => Calendar(games, nowUtc, windowWeeks, 7, FloorToWeekUtc);

    /// <summary>
    /// Start of the UTC calendar day containing <paramref name="instant"/>.
    /// </summary>
    /// <remarks>
    /// Days are UTC, not viewer-local, for the same reason rank snapshots are
    /// capped per UTC day (#907): the whole pipeline buckets on UTC, and a
    /// viewer-local grid would need either a timezone parameter on a cached public
    /// read or client-side re-bucketing of raw games. The consequence is that a
    /// late-night game can land on the next day's cell for players far from UTC.
    /// </remarks>
    public static DateTime FloorToDayUtc(DateTime instant)
        => DateTime.SpecifyKind(instant.Date, DateTimeKind.Utc);

    /// <summary>
    /// Start of the ISO week (Monday 00:00 UTC) containing <paramref name="instant"/>.
    /// </summary>
    public static DateTime FloorToWeekUtc(DateTime instant)
    {
        var day = FloorToDayUtc(instant);
        // DayOfWeek counts from Sunday; ISO weeks start on Monday, so Sunday is
        // six days into its week rather than zero.
        var offset = ((int)day.DayOfWeek + 6) % 7;
        return day.AddDays(-offset);
    }

    /// <summary>
    /// The shared calendar folding: fixed-length UTC slots, a window ending on the
    /// slot that holds "now", clamped at the oldest game we still hold.
    /// </summary>
    /// <param name="games">The player's retained games, in any order.</param>
    /// <param name="nowUtc">Clock reference for the trailing slot.</param>
    /// <param name="slots">How many slots the window asks for.</param>
    /// <param name="slotDays">Length of a slot in days (1 for a day, 7 for a week).</param>
    /// <param name="floor">Maps an instant to the start of its slot.</param>
    private static List<TruemainActivityBucketReadModel> Calendar(
        IReadOnlyList<ActivityGameRow> games,
        DateTime nowUtc,
        int slots,
        int slotDays,
        Func<DateTime, DateTime> floor)
    {
        if (games.Count == 0 || slots <= 0)
        {
            return [];
        }

        var oldestSlot = floor(games.Min(game => game.StartUtc));

        // The trailing slot is normally the one holding "now", but a game must
        // never be dropped for being newer than the clock: ingestion timestamps
        // come from Riot and a few seconds of skew (or a test pinning `nowUtc`
        // behind its fixtures) would otherwise silently lose the newest cell.
        var newestSlot = floor(games.Max(game => game.StartUtc));
        var lastSlot = newestSlot > floor(nowUtc) ? newestSlot : floor(nowUtc);

        var requestedFirstSlot = lastSlot.AddDays(-slotDays * (slots - 1));

        // Clamp: nothing before the oldest game on disk can be described, because
        // retention makes "idle" and "erased" indistinguishable there.
        var firstSlot = oldestSlot > requestedFirstSlot ? oldestSlot : requestedFirstSlot;

        var totals = new Dictionary<DateTime, (int Games, int Wins)>();
        foreach (var game in games)
        {
            var slot = floor(game.StartUtc);
            if (slot < firstSlot)
            {
                continue;
            }

            var current = totals.GetValueOrDefault(slot);
            totals[slot] = (current.Games + 1, current.Wins + (game.Win ? 1 : 0));
        }

        var buckets = new List<TruemainActivityBucketReadModel>();
        for (var slot = firstSlot; slot <= lastSlot; slot = slot.AddDays(slotDays))
        {
            var hit = totals.TryGetValue(slot, out var total);
            buckets.Add(new TruemainActivityBucketReadModel
            {
                Key = FormatSlotKey(slot),
                StartUtc = slot,
                Games = hit ? total.Games : 0,
                Wins = hit ? total.Wins : 0,
                // Null, not 0: an untouched slot has no win rate to report. The
                // OrNull half of the shared arithmetic says exactly that, so the two
                // conventions for "ratio of an empty sample" stop sharing a name.
                WinRate = RateMath.RateOrNull(total.Wins, total.Games),
            });
        }

        return buckets;
    }

    /// <summary>
    /// Cell key for a calendar slot: the ISO date of the slot's first UTC day.
    /// Culture-invariant, so the key is stable whatever the server locale.
    /// </summary>
    private static string FormatSlotKey(DateTime slot)
        => slot.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
