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
    /// How long the measured patch span is held. A patch turns over roughly every
    /// two weeks and the value is global, so this is not a freshness trade-off so
    /// much as a way of asking the question once an hour instead of once a page
    /// view. The visible consequence is bounded and harmless: for up to this long
    /// after the first game of a new patch is ingested, the grid still draws the
    /// previous patch.
    /// </summary>
    private static readonly TimeSpan PatchWindowTtl = TimeSpan.FromMinutes(15);

    public async Task<TruemainActivityReadModel?> GetAsync(string nameTag, CancellationToken ct)
    {
        var account = await resolver.ResolveAsync(nameTag, ct);
        if (account is null)
        {
            return null;
        }

        var queueId = (int)mainAnalysisOptions.Value.QueueId;
        var todayUtc = TruemainActivityBuckets.FloorToDayUtc(DateTime.UtcNow);
        var weekFirstDay = todayUtc.AddDays(-(TruemainActivityBuckets.WeekWindowDays - 1));

        var patchWindow = await GetPatchWindowAsync(queueId, ct);
        var patchFirstDay = patchWindow is null
            ? (DateTime?)null
            : TruemainActivityBuckets.FloorToDayUtc(patchWindow.FirstGameUtc);

        // Only the games either window can actually draw. The week window normally
        // sits inside the patch one, but on the first days of a new patch it reaches
        // back into the previous one.
        var since = patchFirstDay is null || patchFirstDay > weekFirstDay ? weekFirstDay : patchFirstDay.Value;
        var games = await LoadGamesSinceAsync(account.Puuid, queueId, since, ct);

        // A game must never be dropped for being newer than the clock: ingestion
        // timestamps come from Riot, and a few seconds of skew (or a test pinning the
        // clock behind its fixtures) would otherwise silently lose the newest cell.
        var lastDay = games.Count == 0
            ? todayUtc
            : Later(todayUtc, TruemainActivityBuckets.FloorToDayUtc(games[0].StartUtc));

        return new TruemainActivityReadModel
        {
            Patch = patchWindow is null || patchFirstDay is null
                // A database with no parseable patch yet: there is no window to draw,
                // and inventing one from this player's games would answer a different
                // question. The UI states the empty series.
                ? EmptySeries(TruemainActivityKinds.PatchMode, patch: null)
                : Series(
                    TruemainActivityKinds.PatchMode,
                    patchWindow.Patch,
                    TruemainActivityBuckets.ByDay(
                        games,
                        patchFirstDay.Value,
                        Later(TruemainActivityBuckets.FloorToDayUtc(patchWindow.LastGameUtc), lastDay))),
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
    /// The current patch and the span of tracked games played on it, measured over
    /// every player's matches. <see langword="null"/> when no tracked match carries
    /// a parseable patch.
    /// </summary>
    /// <remarks>
    /// "Current" is the patch whose <em>first</em> game is the most recent, which
    /// needs no version parsing and cannot be fooled by a stale straggler being
    /// ingested for an older patch. The group-by runs over the retained window only
    /// (retention holds ~2 patches), and the result is cached — see
    /// <see cref="PatchWindowTtl"/>.
    /// </remarks>
    private async Task<PatchWindow?> GetPatchWindowAsync(int queueId, CancellationToken ct)
    {
        var key = (nameof(TruemainActivityQueryService), nameof(GetPatchWindowAsync), queueId);
        if (cache.TryGetValue(key, out PatchWindow? cached))
        {
            return cached;
        }

        // Projected into an anonymous type, not straight into the record: EF cannot
        // translate a grouping aggregate into a user type's constructor and throws
        // at query time rather than at build time.
        var row = await db.Matches
            .AsNoTracking()
            .Where(match => match.QueueId == queueId && match.Patch != null)
            .GroupBy(match => match.Patch!)
            .Select(group => new
            {
                Patch = group.Key,
                FirstGameUtc = group.Min(match => match.GameStartTimeUtc),
                LastGameUtc = group.Max(match => match.GameStartTimeUtc),
            })
            .OrderByDescending(patch => patch.FirstGameUtc)
            .FirstOrDefaultAsync(ct);

        var window = row is null ? null : new PatchWindow(row.Patch, row.FirstGameUtc, row.LastGameUtc);

        // Cached even when null, so a fresh database does not re-run the group-by on
        // every profile view.
        return cache.Store(key, window, PatchWindowTtl);
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

    /// <summary>
    /// A patch and the span of tracked games played on it.
    /// </summary>
    private sealed record PatchWindow(string Patch, DateTime FirstGameUtc, DateTime LastGameUtc);
}
