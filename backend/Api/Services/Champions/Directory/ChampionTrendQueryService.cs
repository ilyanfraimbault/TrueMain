using Core.Lol.Ranking;
using Core.Options;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Scopes;

namespace TrueMain.Services.Champions.Directory;

public interface IChampionTrendQueryService
{
    /// <summary>
    /// Builds the per-patch winrate / pickrate series for a champion on a
    /// single position, spanning up to the five most recent patches that have
    /// data (oldest → newest). The series is cross-patch by design — no patch
    /// filter is taken, so the caller's active patch never scopes it. It reads
    /// the light per-patch record (<c>champion_patch_stats</c> + the ban pair,
    /// #1956/#1957), the only level that outlives a patch's full aggregates.
    /// The position is the requested one when given and canonical, otherwise
    /// the champion's dominant lane on its latest patch. <paramref name="eloBracket"/>
    /// is the usual rank filter (a tier or <c>TIER_PLUS</c>; null or <c>ALL</c>
    /// for every rank). Returns an empty series (not null) when the champion has
    /// no record, so the caller can render a "not enough data" state without a 404.
    /// </summary>
    Task<ChampionTrendReadModel> GetTrendAsync(
        int championId,
        string? position,
        string? eloBracket,
        CancellationToken ct);
}

public sealed class ChampionTrendQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options,
    IChampionReadCache cache,
    ILogger<ChampionTrendQueryService> logger)
    : IChampionTrendQueryService
{
    // Trend window: the chart reads at a glance, so cap it at the five most
    // recent patches that carry data. Fewer patches simply render a shorter
    // line — the take happens after ordering by release so we always keep the
    // newest end of the history. This window is deliberately cross-patch: the
    // endpoint takes no patch filter, so the page's active patch never scopes
    // the series.
    private const int MaxPatches = 5;
    private const string Surface = "champions-trend";

    public Task<ChampionTrendReadModel> GetTrendAsync(
        int championId,
        string? position,
        string? eloBracket,
        CancellationToken ct)
        => cache.GetOrComputeAsync(
            $"champions:trend:{championId}:{position ?? "auto"}:{EloBracket.ResolveToken(eloBracket)}",
            token => ComputeTrendAsync(championId, position, EloBracket.ResolveFilterOrEmpty(eloBracket), token),
            ct);

    private async Task<ChampionTrendReadModel> ComputeTrendAsync(
        int championId,
        string? position,
        IReadOnlyList<string>? bracketBands,
        CancellationToken ct)
    {
        // Cast the configured queue (a LolQueueId) to the int the record stores,
        // matching ChampionSummariesQueryService so both read the same population.
        var queueId = (int)options.Value.QueueId;

        // Pull every positioned row for this champion in one round-trip and
        // fold the per-(patch, position) totals in memory. A champion spans a
        // handful of patches × at most five lanes × the brackets, so the row set
        // is tiny and re-querying per patch would only add latency. Blank
        // positions are the "no lane" sentinel and never belong on a per-lane
        // trend; Trim() mirrors the directory's scope filter so a whitespace-only
        // position can't leak a phantom lane the directory hides.
        var champRows = await ForTrend(queueId, bracketBands)
            .Where(stat => stat.ChampionId == championId)
            .Where(stat => stat.Position.Trim() != string.Empty)
            .GroupBy(stat => new { stat.Patch, stat.Position })
            .Select(group => new ChampionPatchRow(
                group.Key.Patch,
                group.Key.Position,
                group.Sum(stat => stat.Games),
                group.Sum(stat => stat.Wins)))
            .ToListAsync(ct);

        if (champRows.Count == 0)
        {
            return new ChampionTrendReadModel { ChampionId = championId };
        }

        // One resolver for the whole query: every patch ordering below shares it,
        // so a corrupt gameVersion repeated across rows (or re-read by a second
        // ordering) is warned about once, not once per row.
        var patchSortKeys = new PatchSortKeyResolver(logger, Surface, championId);

        // `position` is already canonicalised by the controller's
        // NormalizePosition (null or a Riot lane string), and ResolvePosition +
        // the empty-result guard below tolerate null/empty, so no second pass.
        var resolvedPosition = ResolvePosition(champRows, position, patchSortKeys);
        if (string.IsNullOrEmpty(resolvedPosition))
        {
            return new ChampionTrendReadModel { ChampionId = championId };
        }

        // The most recent patches (release order) that have a row for this
        // champion on the resolved lane. Ordering keeps the newest end of the
        // history; the final series is re-sorted oldest → newest below.
        var lanePatches = champRows
            .Where(row => string.Equals(row.Position, resolvedPosition, StringComparison.Ordinal))
            .OrderByDescending(row => patchSortKeys.Resolve(row.Patch))
            .Take(MaxPatches)
            .ToList();

        if (lanePatches.Count == 0)
        {
            return new ChampionTrendReadModel
            {
                ChampionId = championId,
                Position = resolvedPosition,
            };
        }

        var patchesInSeries = lanePatches.Select(row => row.Patch).ToList();
        var laneTotals = await LoadLaneTotalsAsync(queueId, bracketBands, resolvedPosition, patchesInSeries, ct);

        // No rank filter reads the stored ALL band, a filter sums its bands (#920)
        // — the directory's rule, through the same helper. Patches older than ban
        // ingestion are simply absent from the lookup and their points keep a null
        // BanRate — a real gap in the series, which the chart renders as "no ban
        // history yet" rather than as a 0%.
        var banScopes = await ChampionBanRateQueries.LoadAsync(db, patchesInSeries, bracketBands, ct);

        var points = lanePatches
            .OrderBy(row => patchSortKeys.Resolve(row.Patch))
            .Select(row =>
            {
                var laneTotal = laneTotals.GetValueOrDefault(row.Patch, 0L);
                return new ChampionTrendPoint
                {
                    Patch = row.Patch,
                    Games = row.Games,
                    WinRate = RateMath.Rate(row.Wins, row.Games),
                    PickRate = RateMath.Rate(row.Games, laneTotal),
                    BanRate = banScopes.GetValueOrDefault(row.Patch)?.RateFor(championId),
                };
            })
            .ToList();

        return new ChampionTrendReadModel
        {
            ChampionId = championId,
            Position = resolvedPosition,
            Points = points,
        };
    }

    private static string ResolvePosition(
        IReadOnlyList<ChampionPatchRow> champRows,
        string? requestedPosition,
        PatchSortKeyResolver patchSortKeys)
    {
        if (!string.IsNullOrWhiteSpace(requestedPosition))
        {
            // Honour the page filter even if the champion has no rows there —
            // an empty series is the correct answer for an off-meta lane,
            // matching the detail page's "respect the active filter" rule.
            return requestedPosition;
        }

        // Default lane = the champion's most-played position on its latest
        // patch with data, so the trend opens on the same slice the detail
        // page lands on when no filter is set.
        var latestPatch = champRows
            .Select(row => row.Patch)
            .OrderByDescending(patchSortKeys.Resolve)
            .First();

        return ChampionAggregateScopeResolver.ResolveDominantPosition(
            champRows
                .Where(row => string.Equals(row.Patch, latestPatch, StringComparison.Ordinal))
                .Select(row => (row.Position, row.Games)));
    }

    /// <summary>
    /// Total TrueMain games on <paramref name="position"/> per patch across
    /// every champion — the pickrate denominators. The record holds every
    /// champion, so the lane total is a GROUP BY over it, with the same
    /// population and bracket set as the numerators and the same definition as
    /// <see cref="ChampionSummaryReadModel.PickRate"/>. Summed as <c>long</c>: a
    /// lane total fans in over every champion on the patch, the widest
    /// accumulator here.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, long>> LoadLaneTotalsAsync(
        int queueId,
        IReadOnlyList<string>? bracketBands,
        string position,
        IReadOnlyList<string> patches,
        CancellationToken ct)
    {
        var totals = await ForTrend(queueId, bracketBands)
            .Where(stat => stat.Position == position && patches.Contains(stat.Patch))
            .GroupBy(stat => stat.Patch)
            .Select(group => new { Patch = group.Key, Games = group.Sum(stat => (long)stat.Games) })
            .ToListAsync(ct);

        return totals.ToDictionary(row => row.Patch, row => row.Games, StringComparer.Ordinal);
    }

    /// <summary>
    /// The light record rows every trend figure reads: the queue, mains only — the
    /// population this read has always described (#1346 added the non-main rows;
    /// every pre-existing read keeps its meaning) — and the rank filter. Null bands
    /// are every rank: the record stores per-tier rows only, so the unfiltered read
    /// is their sum. An empty set (a rejected filter) matches nothing rather than
    /// falling back to every rank.
    /// </summary>
    private IQueryable<ChampionPatchStat> ForTrend(int queueId, IReadOnlyList<string>? bracketBands)
    {
        var stats = db.ChampionPatchStats
            .AsNoTracking()
            .Where(stat => stat.QueueId == queueId && stat.IsMain);

        return bracketBands is null
            ? stats
            : stats.Where(stat => bracketBands.Contains(stat.EloBracket));
    }

    private sealed record ChampionPatchRow(string Patch, string Position, int Games, int Wins);
}
