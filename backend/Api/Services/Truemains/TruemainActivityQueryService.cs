using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

/// <summary>
/// Read path for the profile activity grid (<c>GET /truemains/{nameTag}/activity</c>,
/// #927, reshaped in #1473): a dpm.lol-style heatmap under the LP curve, in three
/// windows over one unit.
/// </summary>
/// <remarks>
/// <para>
/// <b>One unit — the day — and three windows over it.</b> Patch draws every UTC
/// day of the current patch, week the last seven, and day the narrowest window of
/// all, where there are no days left to draw and the cells become the games
/// themselves. All three fold the same <c>match_participants</c> rows for every
/// champion the player queued, read once, so flipping the window cannot show two
/// different answers for the same afternoon.
/// </para>
/// <para>
/// The window that costs something is the patch, because <b>the schema has no
/// patch calendar</b>: a patch's start date is nowhere stored. It is measured
/// instead — the current patch is the one whose first tracked game is the most
/// recent, and its span is that first game through its last. Measured over
/// <em>every</em> player's matches, not this player's, which is the whole point:
/// a day the player sat out at the start of the patch has to be drawn as an idle
/// day, and a per-player bound would silently start the window at their first
/// game. Prod-measured at ~250 ms over the retained window (two patches,
/// ~320k rows), which is why it is cached: it is one global fact that changes
/// once a fortnight, and every profile on the site asks for the same one.
/// </para>
/// <para>
/// Retention (<c>MatchDataRetention:RetainedPatchCount</c>, ~2 patches) cannot
/// make either calendar window lie about an idle day, because both are bounded by
/// matches that are by definition still on disk. What it does mean is that the
/// per-patch career history this endpoint used to serve — read from the frozen
/// per-champion aggregates (#466) — is gone from this card: it answered "how did
/// each patch go" for one champion, which is a different question from the one
/// the grid is now shaped around, and its total was a different population from
/// the day totals sitting next to it.
/// </para>
/// </remarks>
public sealed class TruemainActivityQueryService(
    TrueMainDbContext db,
    TruemainAccountResolver resolver,
    IMemoryCache cache,
    IOptions<MainAnalysisOptions> mainAnalysisOptions) : ITruemainActivityQueryService
{
    /// <summary>
    /// Hard ceiling on the games pulled for one profile. The load is already
    /// bounded to the patch window (a fortnight or so), which even for a grinder is
    /// a few hundred rows — the cap only exists so an unexpectedly long patch
    /// cannot turn a public read into an unbounded scan.
    /// </summary>
    private const int MaxGamesScanned = 1500;

    /// <summary>
    /// How long the measured calendar bounds are held. A patch turns over roughly every
    /// two weeks and the value is global, so this is not a freshness trade-off so
    /// much as a way of asking the question once an hour instead of once a page
    /// view. The visible consequence is bounded and harmless: for up to this long
    /// after the first game of a new patch is ingested, the grid still draws the
    /// previous patch.
    /// </summary>
    private static readonly TimeSpan TrackedBoundsTtl = TimeSpan.FromMinutes(15);

    public async Task<TruemainActivityReadModel?> GetAsync(string nameTag, CancellationToken ct)
    {
        var account = await resolver.ResolveAsync(nameTag, ct);
        if (account is null)
        {
            return null;
        }

        var queueId = (int)mainAnalysisOptions.Value.QueueId;
        var todayUtc = TruemainActivityBuckets.FloorToDayUtc(DateTime.UtcNow);

        var bounds = await GetTrackedBoundsAsync(queueId, ct);
        var patchFirstDay = bounds is null
            ? (DateTime?)null
            : TruemainActivityBuckets.FloorToDayUtc(bounds.PatchFirstGameUtc);

        // The earliest day any window could ask for. Loading from here rather than
        // per window keeps this one query: the month reaches furthest back except in
        // the first days of a patch that opened before it, which cannot happen (a
        // patch is a fortnight), so the min is what the read actually needs.
        var earliestRequested = Earlier(
            todayUtc.AddDays(-(TruemainActivityBuckets.MonthWindowDays - 1)),
            patchFirstDay ?? todayUtc);
        var games = await LoadGamesSinceAsync(account.Puuid, queueId, earliestRequested, ct);

        // A game must never be dropped for being newer than the clock: ingestion
        // timestamps come from Riot, and a few seconds of skew (or a test pinning the
        // clock behind its fixtures) would otherwise silently lose the newest cell.
        var lastDay = games.Count == 0
            ? todayUtc
            : Later(todayUtc, TruemainActivityBuckets.FloorToDayUtc(games[0].StartUtc));

        return new TruemainActivityReadModel
        {
            Month = Series(
                TruemainActivityKinds.MonthMode,
                patch: null,
                TruemainActivityBuckets.ByDay(
                    games,
                    MonthFirstDay(bounds, lastDay),
                    lastDay)),
            Patch = bounds is null || patchFirstDay is null
                // A database with no parseable patch yet: there is no window to draw,
                // and inventing one from this player's games would answer a different
                // question. The UI states the empty series.
                ? EmptySeries(TruemainActivityKinds.PatchMode, patch: null)
                : Series(
                    TruemainActivityKinds.PatchMode,
                    bounds.Patch,
                    TruemainActivityBuckets.ByDay(
                        games,
                        patchFirstDay.Value,
                        Later(TruemainActivityBuckets.FloorToDayUtc(bounds.PatchLastGameUtc), lastDay))),
            Week = Series(
                TruemainActivityKinds.WeekMode,
                patch: null,
                TruemainActivityBuckets.ByDay(games, lastDay.AddDays(-(TruemainActivityBuckets.WeekWindowDays - 1)), lastDay)),
            Day = Series(
                TruemainActivityKinds.DayMode,
                patch: null,
                TruemainActivityBuckets.ByGame(games, lastDay)),
        };
    }

    /// <summary>
    /// First day of the month window: thirty days back, but never past the oldest
    /// day anyone still has data for.
    /// </summary>
    /// <remarks>
    /// This is the one window wide enough for retention to bite. Match rows are
    /// hard-deleted past <c>MatchDataRetention:RetainedPatchCount</c> patches, which
    /// is roughly a month — so a thirty-day window can reach a day where "did not
    /// queue" and "no longer stored" are indistinguishable, and drawing it as an
    /// idle tile would be a fabricated claim about the player. Clamped to the oldest
    /// retained game *anyone* has, the same global measurement the patch window is
    /// bounded by; the grid then simply starts later and says nothing it cannot back.
    /// </remarks>
    private static DateTime MonthFirstDay(TrackedBounds? bounds, DateTime lastDay)
    {
        var requested = lastDay.AddDays(-(TruemainActivityBuckets.MonthWindowDays - 1));
        if (bounds is null)
        {
            return requested;
        }

        return Later(requested, TruemainActivityBuckets.FloorToDayUtc(bounds.OldestGameUtc));
    }

    /// <summary>
    /// The player's ranked games from <paramref name="sinceUtc"/> on, newest first.
    /// </summary>
    /// <remarks>
    /// Scoped to the tracked ranked queue, the same population the rest of the
    /// profile counts. The index path is <c>IX_match_participants_puuid_match</c>
    /// followed by a PK lookup per match, so the cost tracks the player's retained
    /// game count.
    /// </remarks>
    private async Task<IReadOnlyList<ActivityGameRow>> LoadGamesSinceAsync(
        string puuid,
        int queueId,
        DateTime sinceUtc,
        CancellationToken ct)
        => await (
            from participant in db.MatchParticipants.AsNoTracking()
            join match in db.Matches.AsNoTracking() on participant.MatchId equals match.Id
            where participant.Puuid == puuid
                && match.QueueId == queueId
                && match.GameStartTimeUtc >= sinceUtc
            // The match id breaks ties on the start time: this is a Take over an
            // otherwise non-total order, so games sharing a start second could enter and
            // leave the scanned window between two identical requests — and a grid that
            // reshuffles reads as a data change (ChampionDominantLaneFilter).
            orderby match.GameStartTimeUtc descending, match.Id descending
            select new ActivityGameRow(
                match.Id,
                match.GameStartTimeUtc,
                participant.Win,
                participant.ChampionId))
            .Take(MaxGamesScanned)
            .ToListAsync(ct);

    /// <summary>
    /// The calendar facts the windows are bounded by, measured over every player's
    /// matches: the current patch with the span of games played on it, and the
    /// oldest game retention still holds. <see langword="null"/> when no tracked
    /// match carries a parseable patch.
    /// </summary>
    /// <remarks>
    /// One group-by answers both. It returns a row per retained patch — retention
    /// holds ~2, so this is a two-row result, not a scan the API pages through —
    /// from which "current" is the patch whose <em>first</em> game is the most
    /// recent (no version parsing, and a stale straggler ingested for an older patch
    /// cannot fool it) and the retention floor is the earliest first-game across all
    /// of them. Cached — see <see cref="TrackedBoundsTtl"/>.
    /// </remarks>
    private async Task<TrackedBounds?> GetTrackedBoundsAsync(int queueId, CancellationToken ct)
    {
        var key = (nameof(TruemainActivityQueryService), nameof(GetTrackedBoundsAsync), queueId);
        if (cache.TryGetValue(key, out TrackedBounds? cached))
        {
            return cached;
        }

        // Projected into an anonymous type, not straight into the record: EF cannot
        // translate a grouping aggregate into a user type's constructor and throws
        // at query time rather than at build time.
        var rows = await db.Matches
            .AsNoTracking()
            .Where(match => match.QueueId == queueId && match.Patch != null)
            .GroupBy(match => match.Patch!)
            .Select(group => new
            {
                Patch = group.Key,
                FirstGameUtc = group.Min(match => match.GameStartTimeUtc),
                LastGameUtc = group.Max(match => match.GameStartTimeUtc),
            })
            .ToListAsync(ct);

        var current = rows.MaxBy(patch => patch.FirstGameUtc);
        var bounds = current is null
            ? null
            : new TrackedBounds(
                current.Patch,
                current.FirstGameUtc,
                current.LastGameUtc,
                rows.Min(patch => patch.FirstGameUtc));

        // Cached even when null, so a fresh database does not re-run the group-by on
        // every profile view.
        return cache.Store(key, bounds, TrackedBoundsTtl);
    }

    private static TruemainActivitySeriesReadModel Series(
        string mode,
        string? patch,
        IReadOnlyList<TruemainActivityBucketReadModel> buckets)
    {
        var games = buckets.Sum(bucket => bucket.Games);
        var wins = buckets.Sum(bucket => bucket.Wins);

        return new TruemainActivitySeriesReadModel
        {
            Mode = mode,
            Patch = patch,
            // Read off the emitted cells rather than computed a second time, so the
            // range and the squares behind it cannot drift apart.
            CoverageFromUtc = buckets.Count == 0 ? null : buckets[0].StartUtc,
            CoverageToUtc = buckets.Count == 0 ? null : buckets[^1].StartUtc,
            Buckets = buckets,
            Games = games,
            Wins = wins,
            // Null rather than 0 on an empty window — see the read model.
            WinRate = games == 0 ? null : (double)wins / games,
        };
    }

    private static TruemainActivitySeriesReadModel EmptySeries(string mode, string? patch)
        => Series(mode, patch, []);

    private static DateTime Later(DateTime left, DateTime right) => left > right ? left : right;

    private static DateTime Earlier(DateTime left, DateTime right) => left < right ? left : right;

    /// <summary>
    /// The current patch with the span of tracked games played on it, plus the
    /// oldest tracked game still on disk — the floor no window may reach past.
    /// </summary>
    private sealed record TrackedBounds(
        string Patch,
        DateTime PatchFirstGameUtc,
        DateTime PatchLastGameUtc,
        DateTime OldestGameUtc);
}
