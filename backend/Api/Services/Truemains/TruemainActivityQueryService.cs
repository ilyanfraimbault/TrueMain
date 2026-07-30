using Core.Lol.Patches;
using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

/// <summary>
/// Read path for the profile activity grid (<c>GET /truemains/{nameTag}/activity</c>,
/// #927): a dpm.lol-style heatmap under the LP curve, in four granularities.
/// </summary>
/// <remarks>
/// <para>
/// The interesting part of this endpoint is that its four modes cannot come from
/// one place. <c>match_participants</c> holds a game's date, so it is the only
/// source that can answer "which days did you play" — but retention hard-deletes
/// it past <c>MatchDataRetention:RetainedPatchCount</c> patches (~2), so it cannot
/// answer anything about last season. <c>champion_aggregate_scopes</c> is frozen
/// forever (#466) and therefore holds the whole career — but its grain is
/// (account, champion, patch, …), so it can only answer per patch, and only for
/// one champion at a time.
/// </para>
/// <para>
/// So the modes are not four views of one dataset, and the response says so
/// rather than hiding it: the game / day / week series are
/// <c>source=matches, scope=allChampions, retentionBounded=true</c> with an
/// explicit coverage range, and the patch series is
/// <c>source=aggregates, scope=champion</c> with no coverage range and no
/// retention bound. The alternative — scoping every mode to one champion, or
/// pretending the aggregate can be split by day — would make the numbers agree by
/// making them wrong.
/// </para>
/// <para>
/// The patch series deliberately reuses <see cref="MainDedication"/>'s champion
/// pick and sums the exact same rows its <c>careerGames</c> / <c>patchSpan</c>
/// come from. That is not a coincidence to preserve loosely: the dedication card
/// sits a few centimetres above this grid on the same page, so
/// <c>patch.games == dedication.careerGames</c> and
/// <c>patch.buckets.length == dedication.patchSpan</c> are invariants a reader can
/// check by eye.
/// </para>
/// </remarks>
public sealed class TruemainActivityQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> mainAnalysisOptions) : ITruemainActivityQueryService
{
    /// <summary>
    /// Hard ceiling on the games pulled for the match-sourced series. Retention
    /// already bounds this to roughly two patches, which even for a grinder is a
    /// few hundred rows — the cap only exists so an unexpectedly wide retention
    /// window (or a preprod with retention disabled) cannot turn a public read into
    /// an unbounded scan. It is comfortably above every window below, so it never
    /// truncates a grid in practice.
    /// </summary>
    private const int MaxGamesScanned = 1500;

    public async Task<TruemainActivityReadModel?> GetAsync(string nameTag, CancellationToken ct)
    {
        if (!NameTagParser.TryParse(nameTag, out var parsed))
        {
            return null;
        }

        // Same resolver as the profile / matches / rank-history routes (most
        // recently active row on a cross-region Riot-id collision), so every panel
        // on the page is talking about the same account.
        var account = await db.RiotAccounts
            .AsNoTracking()
            .Where(a => a.GameName == parsed.GameName && a.TagLine == parsed.TagLine)
            .OrderByDescending(a => a.LastMatchIngestAtUtc ?? a.UpdatedAtUtc)
            .Select(a => new { a.Id, a.Puuid })
            .FirstOrDefaultAsync(ct);

        if (account is null)
        {
            return null;
        }

        var queueId = (int)mainAnalysisOptions.Value.QueueId;
        var nowUtc = DateTime.UtcNow;

        var games = await LoadRetainedGamesAsync(account.Puuid, queueId, ct);
        var patch = await LoadPatchSeriesAsync(account.Id, queueId, nowUtc, ct);

        return new TruemainActivityReadModel
        {
            Game = MatchSeries(
                TruemainActivityKinds.GameMode,
                TruemainActivityBuckets.ByGame(games, TruemainActivityBuckets.GameWindow)),
            Day = MatchSeries(
                TruemainActivityKinds.DayMode,
                TruemainActivityBuckets.ByDay(games, nowUtc, TruemainActivityBuckets.DayWindowDays)),
            Week = MatchSeries(
                TruemainActivityKinds.WeekMode,
                TruemainActivityBuckets.ByWeek(games, nowUtc, TruemainActivityBuckets.WeekWindowWeeks)),
            Patch = patch,
        };
    }

    /// <summary>
    /// The player's ranked games still on disk, newest first.
    /// </summary>
    /// <remarks>
    /// Scoped to the tracked ranked queue rather than to the match feed's
    /// "anything but Arena" predicate: the patch series reads aggregates that are
    /// hard-scoped to that queue, and two modes of one grid disagreeing about which
    /// games count would be exactly the failure this endpoint is shaped to avoid.
    /// The index path is <c>IX_match_participants_puuid_match</c> followed by a PK
    /// lookup per match, so the cost tracks the player's retained game count.
    /// </remarks>
    private async Task<IReadOnlyList<ActivityGameRow>> LoadRetainedGamesAsync(
        string puuid,
        int queueId,
        CancellationToken ct)
        => await (
            from participant in db.MatchParticipants.AsNoTracking()
            join match in db.Matches.AsNoTracking() on participant.MatchId equals match.Id
            where participant.Puuid == puuid && match.QueueId == queueId
            orderby match.GameStartTimeUtc descending
            select new ActivityGameRow(
                match.Id,
                match.GameStartTimeUtc,
                participant.Win,
                participant.ChampionId))
            .Take(MaxGamesScanned)
            .ToListAsync(ct);

    /// <summary>
    /// Per-patch history on the player's signature champion, read from the frozen
    /// aggregate scopes.
    /// </summary>
    /// <remarks>
    /// The champion comes from <see cref="MainDedication"/> — the single place that
    /// decides what a player's signature champion is — and the scope filter is the
    /// one that helper's career lateral uses (account + champion + ranked queue, no
    /// platform / position / bracket narrowing). Anything narrower would make this
    /// grid's total disagree with the dedication card's <c>careerGames</c> sitting
    /// right above it.
    /// </remarks>
    private async Task<TruemainActivitySeriesReadModel> LoadPatchSeriesAsync(
        Guid accountId,
        int queueId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var dedication = await MainDedication.FetchAsync(
            db,
            [accountId],
            championId: null,
            nowUtc,
            mainAnalysisOptions.Value.PlayRateFloor,
            ct);

        if (!dedication.TryGetValue(accountId, out var signature))
        {
            // No classified main: there is no champion to scope a patch history to.
            // An empty series with a null championId is the honest answer — widening
            // it to every champion would silently answer a different question, and
            // the aggregate cannot be split by day anyway.
            return EmptySeries(
                TruemainActivityKinds.PatchMode,
                TruemainActivityKinds.AggregatesSource,
                TruemainActivityKinds.ChampionScope,
                championId: null,
                retentionBounded: false);
        }

        var championId = signature.ChampionId;

        var rows = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.RiotAccountId == accountId
                && scope.ChampionId == championId
                && scope.QueueId == queueId)
            .GroupBy(scope => scope.GameVersion)
            .Select(group => new
            {
                Patch = group.Key,
                Games = group.Sum(scope => scope.Games),
                Wins = group.Sum(scope => scope.Wins),
            })
            .ToListAsync(ct);

        // A scope row with no games would render as an empty patch cell, which on
        // this series would read as "the patch existed and you sat it out" — a claim
        // the aggregate cannot make (it only ever records patches that were played).
        // Such a row should not exist; drop it rather than draw it.
        var patchRows = rows.Where(row => row.Games > 0).ToList();

        // Patches are stored as `major.minor` strings, so ordering has to go through
        // the numeric key — "15.10" sorts before "15.9" as text.
        var buckets = patchRows
            .OrderBy(row => PatchOrderKey(row.Patch))
            .Select(row => new TruemainActivityBucketReadModel
            {
                Key = row.Patch,
                // A patch has no stored start instant; only its scopes' last game is
                // recorded, and that is an end, not a start. Left null rather than
                // filled with an approximation.
                StartUtc = null,
                Games = row.Games,
                Wins = row.Wins,
                WinRate = (double)row.Wins / row.Games,
            })
            .ToList();

        return Series(
            TruemainActivityKinds.PatchMode,
            TruemainActivityKinds.AggregatesSource,
            TruemainActivityKinds.ChampionScope,
            championId,
            retentionBounded: false,
            // A patch list is not a date range: the extent that matters here is
            // "which patches", and the client already has them.
            coverageFromUtc: null,
            coverageToUtc: null,
            buckets);
    }

    /// <summary>
    /// Wraps a match-sourced folding into its series, deriving the coverage range
    /// from the emitted cells.
    /// </summary>
    /// <remarks>
    /// The range is read off the buckets rather than computed a second time, so the
    /// "we can speak for this window" claim and the cells that back it cannot drift
    /// apart. An empty folding reports a null range, which is what lets the UI say
    /// "nothing retained" instead of drawing an empty month.
    /// </remarks>
    private static TruemainActivitySeriesReadModel MatchSeries(
        string mode,
        IReadOnlyList<TruemainActivityBucketReadModel> buckets)
        => Series(
            mode,
            TruemainActivityKinds.MatchesSource,
            TruemainActivityKinds.AllChampionsScope,
            championId: null,
            retentionBounded: true,
            coverageFromUtc: buckets.Count == 0 ? null : buckets[0].StartUtc,
            coverageToUtc: buckets.Count == 0 ? null : buckets[^1].StartUtc,
            buckets);

    private static TruemainActivitySeriesReadModel Series(
        string mode,
        string source,
        string scope,
        int? championId,
        bool retentionBounded,
        DateTime? coverageFromUtc,
        DateTime? coverageToUtc,
        IReadOnlyList<TruemainActivityBucketReadModel> buckets)
    {
        var games = buckets.Sum(bucket => bucket.Games);
        var wins = buckets.Sum(bucket => bucket.Wins);

        return new TruemainActivitySeriesReadModel
        {
            Mode = mode,
            Source = source,
            Scope = scope,
            ChampionId = championId,
            RetentionBounded = retentionBounded,
            CoverageFromUtc = coverageFromUtc,
            CoverageToUtc = coverageToUtc,
            Buckets = buckets,
            Games = games,
            Wins = wins,
            // Null rather than 0 on an empty series — see the read model.
            WinRate = games == 0 ? null : (double)wins / games,
        };
    }

    private static TruemainActivitySeriesReadModel EmptySeries(
        string mode,
        string source,
        string scope,
        int? championId,
        bool retentionBounded)
        => Series(mode, source, scope, championId, retentionBounded, null, null, []);

    /// <summary>
    /// Numeric ordering key for a stored <c>gameVersion</c>. A value that does not
    /// parse sorts as the oldest patch, matching
    /// <c>PatchSortKeyResolver</c>'s established behaviour (#394) — that resolver
    /// itself is not reused because it is champion-scoped and exists to warn once
    /// per champion query, which is not this endpoint's shape.
    /// </summary>
    private static (int Major, int Minor) PatchOrderKey(string gameVersion)
        => PatchVersion.TryParse(gameVersion, out var version)
            ? (version.Major, version.Minor)
            : (0, 0);
}
