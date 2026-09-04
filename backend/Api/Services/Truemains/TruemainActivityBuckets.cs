using System.Globalization;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

/// <summary>
/// One game of a player's retained history, as the activity read needs it.
/// </summary>
/// <param name="MatchId">Riot match id — the day window's cell key.</param>
/// <param name="StartUtc">Game start; the only thing the calendar foldings bucket on.</param>
/// <param name="Win">Whether the player won.</param>
/// <param name="ChampionId">Champion the player was on.</param>
internal sealed record ActivityGameRow(string MatchId, DateTime StartUtc, bool Win, int ChampionId);

/// <summary>
/// The activity grid's bucketing — pure, so the window boundaries can be tested
/// without a database. Every folding here reads the same game list, which is what
/// makes the three windows structurally incapable of disagreeing about the same
/// afternoon.
/// </summary>
/// <remarks>
/// <para>
/// <b>The unit is the day, and every day of the window is drawn.</b> A calendar
/// window emits a cell for each UTC day it spans, whether or not the player
/// queued — the run of idle tiles between two sessions is the shape of a patch,
/// and skipping them leaves a hole where a fact should be. Where the earlier
/// version clamped its window to the player's own oldest game, the window is now
/// given from outside (the patch's measured span, or the last seven days), so a
/// player who did not play the first week of the patch sees that week.
/// </para>
/// <para>
/// <b>An empty day and a lost day are different facts.</b> A cell with no games
/// carries <c>Games = 0</c> and a <see langword="null"/> win rate; only a cell
/// with games can carry <c>0.0</c>. The read models never fill a gap with a zero.
/// </para>
/// <para>
/// <b>Retention cannot make a calendar window lie.</b> <c>match_participants</c>
/// is hard-deleted past <c>MatchDataRetention:RetainedPatchCount</c> patches
/// (~2), so a day before that could be "erased" rather than "idle". Both windows
/// here sit comfortably inside it by construction: the week window is seven days,
/// and the patch window's own start is read off matches that are, by definition,
/// still on disk.
/// </para>
/// </remarks>
internal static class TruemainActivityBuckets
{
    /// <summary>Days in the week window, today included.</summary>
    public const int WeekWindowDays = 7;

    /// <summary>
    /// One cell per UTC day from <paramref name="firstDayUtc"/> through
    /// <paramref name="lastDayUtc"/>, both inclusive and both expected to already
    /// be day starts. Days with no games are emitted, and games outside the range
    /// are ignored.
    /// </summary>
    public static IReadOnlyList<TruemainActivityBucketReadModel> ByDay(
        IReadOnlyList<ActivityGameRow> games,
        DateTime firstDayUtc,
        DateTime lastDayUtc)
    {
        if (lastDayUtc < firstDayUtc)
        {
            return [];
        }

        var totals = new Dictionary<DateTime, (int Games, int Wins)>();
        foreach (var game in games)
        {
            var day = FloorToDayUtc(game.StartUtc);
            if (day < firstDayUtc || day > lastDayUtc)
            {
                continue;
            }

            var current = totals.GetValueOrDefault(day);
            totals[day] = (current.Games + 1, current.Wins + (game.Win ? 1 : 0));
        }

        var buckets = new List<TruemainActivityBucketReadModel>();
        for (var day = firstDayUtc; day <= lastDayUtc; day = day.AddDays(1))
        {
            var total = totals.GetValueOrDefault(day);
            buckets.Add(new TruemainActivityBucketReadModel
            {
                Key = FormatDayKey(day),
                StartUtc = day,
                Games = total.Games,
                Wins = total.Wins,
                // Null, not 0: an untouched day has no win rate to report. The
                // OrNull half of the shared arithmetic says exactly that, so the two
                // conventions for "ratio of an empty sample" stop sharing a name.
                WinRate = RateMath.RateOrNull(total.Wins, total.Games),
            });
        }

        return buckets;
    }

    /// <summary>
    /// One cell per game played on the UTC day containing
    /// <paramref name="dayUtc"/>, oldest first. Empty on a rest day, which the UI
    /// states in words — there is no such thing as an "idle game" to draw.
    /// </summary>
    public static IReadOnlyList<TruemainActivityBucketReadModel> ByGame(
        IReadOnlyList<ActivityGameRow> games,
        DateTime dayUtc)
    {
        var day = FloorToDayUtc(dayUtc);

        return games
            .Where(game => FloorToDayUtc(game.StartUtc) == day)
            // The match id breaks ties on the start instant so two games that
            // started in the same second cannot swap places between two requests.
            .OrderBy(game => game.StartUtc)
            .ThenBy(game => game.MatchId, StringComparer.Ordinal)
            .Select(game => new TruemainActivityBucketReadModel
            {
                Key = game.MatchId,
                StartUtc = game.StartUtc,
                Games = 1,
                Wins = game.Win ? 1 : 0,
                // A single game is a decided fact, so the rate is exactly 0 or 1
                // — never null. The null case only exists for empty calendar
                // cells, which this window cannot produce.
                WinRate = game.Win ? 1d : 0d,
                ChampionId = game.ChampionId,
            })
            .ToList();
    }

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
    /// Cell key for a calendar day: its ISO date. Culture-invariant, so the key is
    /// stable whatever the server locale.
    /// </summary>
    private static string FormatDayKey(DateTime day)
        => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
