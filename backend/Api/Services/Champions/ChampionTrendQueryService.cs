using Core.Lol.Patches;
using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public sealed class ChampionTrendQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options)
    : IChampionTrendQueryService
{
    // Trend window: the chart reads at a glance, so cap it at the five most
    // recent patches that carry data. Fewer patches simply render a shorter
    // line — the take happens after ordering by release so we always keep the
    // newest end of the history. This window is deliberately cross-patch: the
    // endpoint takes no patch filter, so the page's active patch never scopes
    // the series.
    private const int MaxPatches = 5;

    public async Task<ChampionTrendReadModel> GetTrendAsync(
        int championId,
        string? position,
        CancellationToken ct)
    {
        // Cast the configured queue (a LolQueueId) to the int the persisted
        // scope stores, matching ChampionSummariesQueryService so both read the
        // same rows.
        var queueId = (int)options.Value.QueueId;

        // Pull every positioned scope for this champion in one round-trip and
        // fold the per-(patch, position) totals in memory. A champion spans a
        // handful of patches × at most five lanes, so the row set is tiny and
        // re-querying per patch would only add latency. Blank positions are
        // the "no lane" sentinel and never belong on a per-lane trend; Trim()
        // mirrors the directory's scope filter so a whitespace-only position
        // can't leak a phantom lane the directory hides.
        var champRows = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.ChampionId == championId && scope.QueueId == queueId)
            .Where(scope => scope.Position.Trim() != string.Empty)
            .GroupBy(scope => new { scope.GameVersion, scope.Position })
            .Select(group => new ChampionPatchRow(
                group.Key.GameVersion,
                group.Key.Position,
                group.Sum(scope => scope.Games),
                group.Sum(scope => scope.Wins)))
            .ToListAsync(ct);

        if (champRows.Count == 0)
        {
            return new ChampionTrendReadModel { ChampionId = championId };
        }

        // `position` is already canonicalised by the controller's
        // NormalizePosition (null or a Riot lane string), and ResolvePosition +
        // the empty-result guard below tolerate null/empty, so no second pass.
        var resolvedPosition = ResolvePosition(champRows, position);
        if (string.IsNullOrEmpty(resolvedPosition))
        {
            return new ChampionTrendReadModel { ChampionId = championId };
        }

        // The most recent patches (release order) that have a row for this
        // champion on the resolved lane. Ordering keeps the newest end of the
        // history; the final series is re-sorted oldest → newest below.
        var lanePatches = champRows
            .Where(row => string.Equals(row.Position, resolvedPosition, StringComparison.Ordinal))
            .OrderByDescending(row => ParsePatch(row.Patch))
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

        var laneTotals = await LoadLaneTotalsAsync(
            queueId, resolvedPosition, lanePatches.Select(row => row.Patch).ToList(), ct);

        var points = lanePatches
            .OrderBy(row => ParsePatch(row.Patch))
            .Select(row =>
            {
                var laneTotal = laneTotals.GetValueOrDefault(row.Patch, 0L);
                return new ChampionTrendPoint
                {
                    Patch = row.Patch,
                    Games = row.Games,
                    WinRate = row.Games == 0 ? 0 : (double)row.Wins / row.Games,
                    PickRate = laneTotal == 0 ? 0 : (double)row.Games / laneTotal,
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
        string? requestedPosition)
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
            .OrderByDescending(ParsePatch)
            .First();

        return ChampionAggregateScopeResolver.ResolveDominantPosition(
            champRows
                .Where(row => string.Equals(row.Patch, latestPatch, StringComparison.Ordinal))
                .Select(row => (row.Position, row.Games)));
    }

    /// <summary>
    /// Total TrueMain games on <paramref name="position"/> per patch across
    /// every champion — the pickrate denominators. Computed in SQL with a
    /// single GROUP BY over the requested patches so the share each champion
    /// row gets matches <see cref="ChampionSummaryReadModel.PickRate"/>
    /// exactly. Summed as <c>long</c>: a lane total fans in over every
    /// champion on the patch, the widest accumulator here.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, long>> LoadLaneTotalsAsync(
        int queueId,
        string position,
        IReadOnlyList<string> patches,
        CancellationToken ct)
    {
        var totals = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.QueueId == queueId && scope.Position == position)
            .Where(scope => patches.Contains(scope.GameVersion))
            .GroupBy(scope => scope.GameVersion)
            .Select(group => new { Patch = group.Key, Games = group.Sum(scope => (long)scope.Games) })
            .ToListAsync(ct);

        return totals.ToDictionary(row => row.Patch, row => row.Games, StringComparer.Ordinal);
    }

    private static (int Major, int Minor) ParsePatch(string gameVersion)
        => PatchVersion.TryParse(gameVersion, out var version)
            ? (version.Major, version.Minor)
            : (0, 0);

    private sealed record ChampionPatchRow(string Patch, string Position, int Games, int Wins);
}
