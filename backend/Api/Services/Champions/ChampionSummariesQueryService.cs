using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public sealed class ChampionSummariesQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options,
    IMemoryCache cache) : IChampionSummariesQueryService
{
    // The directory list is the same payload for every caller of GET /champions
    // on a given patch and stays valid for the few seconds between ingestor
    // flushes. Caching keyed on the resolved patch means the row-fanning groupby
    // below is paid once per (patch, window) instead of once per request.
    private static readonly TimeSpan SummariesCacheTtl = TimeSpan.FromSeconds(30);

    public async Task<IReadOnlyList<ChampionSummaryReadModel>> GetAllSummariesAsync(string? patch, CancellationToken ct)
    {
        // Resolve the patch up-front so the cache key is the canonical patch
        // value (e.g. "16.10") rather than the raw query string. This way
        // requests with `patch=null` and `patch=16.10` share the same entry
        // when the latter happens to be the active patch, and non-canonical
        // inputs (trailing whitespace, capitalization differences if patches
        // ever carry letters) can't fan out into redundant cache entries.
        var activePatch = await ResolveActivePatchAsync(patch, ct);
        if (string.IsNullOrEmpty(activePatch))
        {
            return [];
        }

        return await GetOrComputeSummariesAsync(activePatch, ct);
    }

    public async Task<ChampionSummariesPagedResponse> GetSummariesPageAsync(
        string? patch,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var activePatch = await ResolveActivePatchAsync(patch, ct);
        if (string.IsNullOrEmpty(activePatch))
        {
            return new ChampionSummariesPagedResponse
            {
                Items = [],
                TotalCount = 0,
                Page = page,
                PageSize = pageSize,
            };
        }

        // Pull the full sorted list from the cache (or compute + cache on
        // miss) and slice in memory. Slicing 50 of ~500 rows is cheap and
        // keeps the cache key patch-only — caching every (patch, page, size)
        // combination would fan out for no win since the list itself stays
        // identical across page requests.
        var all = await GetOrComputeSummariesAsync(activePatch, ct);

        var skip = checked((page - 1) * pageSize);
        var items = skip >= all.Count
            ? (IReadOnlyList<ChampionSummaryReadModel>)[]
            : all.Skip(skip).Take(pageSize).ToList();

        return new ChampionSummariesPagedResponse
        {
            Items = items,
            TotalCount = all.Count,
            Page = page,
            PageSize = pageSize,
        };
    }

    private async Task<IReadOnlyList<ChampionSummaryReadModel>> GetOrComputeSummariesAsync(
        string activePatch,
        CancellationToken ct)
    {
        var cacheKey = $"champions:summaries:{activePatch}";
        if (cache.TryGetValue<IReadOnlyList<ChampionSummaryReadModel>>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var summaries = await ComputeAllSummariesAsync(activePatch, ct);
        cache.Set(cacheKey, summaries, SummariesCacheTtl);
        return summaries;
    }

    private async Task<string?> ResolveActivePatchAsync(string? requestedPatch, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(requestedPatch))
        {
            return requestedPatch;
        }

        var distinctPatches = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.QueueId == options.Value.QueueId)
            .Select(scope => scope.GameVersion)
            .Distinct()
            .ToListAsync(ct);
        return ChampionAggregateScopeResolver.ResolvePatchVersion(distinctPatches, requestedPatch: null);
    }

    private async Task<IReadOnlyList<ChampionSummaryReadModel>> ComputeAllSummariesAsync(string activePatch, CancellationToken ct)
    {
        // Filtering the row pull on GameVersion in SQL is what makes the
        // per-patch caching worthwhile — without it every cache miss would
        // re-scan the whole aggregate table for the queue.
        var rows = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.QueueId == options.Value.QueueId)
            .Where(scope => scope.GameVersion == activePatch)
            .Select(scope => new ChampionSummaryRow(
                scope.ChampionId,
                scope.GameVersion,
                scope.Position,
                scope.Games,
                scope.Wins,
                scope.RiotAccountId,
                scope.AggregatedAtUtc))
            .ToListAsync(ct);

        var scoped = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Position))
            .ToList();

        if (scoped.Count == 0)
        {
            return [];
        }

        // Top build per (champion, position) — loaded once for the whole
        // patch so the directory renders keystone / secondary tree / item
        // sequence inline without paying a per-row build query.
        var topBuilds = await LoadTopBuildsAsync(activePatch, ct);

        // Denominators come from the already-loaded scope rows: lane totals
        // for PickRate and champion totals for LanePlayRate. PickRate is the
        // share of TrueMain games at this lane that picked this champion —
        // a main-population signal, not a meta-wide one (the meta-wide ratio
        // would need a full match_participants scan, which doesn't scale).
        // Sum as long up-front: lane totals fan in over every scoped row on
        // the patch, so this is the widest accumulator in the method — the
        // only one with any plausible long-term risk of int overflow.
        var laneTotals = scoped
            .GroupBy(row => row.Position, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(row => (long)row.Games), StringComparer.Ordinal);
        var championTotals = scoped
            .GroupBy(row => row.ChampionId)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Games));

        return scoped
            .GroupBy(row => (row.ChampionId, row.Position))
            .Select(group =>
            {
                var games = group.Sum(row => row.Games);
                var wins = group.Sum(row => row.Wins);
                var championTotal = championTotals.GetValueOrDefault(group.Key.ChampionId);
                var laneTotal = laneTotals.GetValueOrDefault(group.Key.Position, 0L);

                topBuilds.TryGetValue((group.Key.ChampionId, group.Key.Position), out var topBuild);
                return new ChampionSummaryReadModel
                {
                    ChampionId = group.Key.ChampionId,
                    Games = games,
                    Wins = wins,
                    WinRate = games == 0 ? 0 : (double)wins / games,
                    PickRate = laneTotal == 0 ? 0 : (double)games / laneTotal,
                    LanePlayRate = championTotal == 0 ? 0 : (double)games / championTotal,
                    TrueMainCount = group.Select(row => row.RiotAccountId).Distinct().Count(),
                    Position = group.Key.Position,
                    PatchVersion = activePatch,
                    LastUpdatedAtUtc = group.Max(row => row.AggregatedAtUtc),
                    TopBuild = topBuild,
                };
            })
            .OrderByDescending(summary => summary.PickRate)
            .ThenBy(summary => summary.ChampionId)
            .ThenBy(summary => summary.Position, StringComparer.Ordinal)
            .ToList();
    }

    private sealed record ChampionSummaryRow(
        int ChampionId,
        string GameVersion,
        string Position,
        int Games,
        int Wins,
        Guid RiotAccountId,
        DateTime AggregatedAtUtc);

    /// <summary>
    /// Resolves the dominant <c>(firstItem, primaryKeystone)</c> bucket for
    /// every <c>(champion, position)</c> pair on
    /// <paramref name="activePatch"/>, then computes the consensus item
    /// path for that bucket via <see cref="ChampionBuildPathAnalyzer"/> —
    /// the same tree-walk used to build the "core" path on the champion
    /// detail page, so the path shown on each list row matches the path on
    /// that champion's detail page for the same slice.
    /// </summary>
    private async Task<IReadOnlyDictionary<(int ChampionId, string Position), TopBuildReadModel>> LoadTopBuildsAsync(
        string activePatch,
        CancellationToken ct)
    {
        var queueId = options.Value.QueueId;

        // Aggregate every pattern row to one bucket per
        // (champion, position, build, rune-page). Collapsing in SQL keeps
        // the materialised set bounded — for a busy patch it's tens of
        // thousands of bucket rows instead of millions of raw patterns.
        var grouped = await db.ChampionAggregatePatterns
            .AsNoTracking()
            .Join(
                db.ChampionAggregateScopes.AsNoTracking()
                    .Where(scope => scope.QueueId == queueId && scope.GameVersion == activePatch),
                pattern => pattern.ScopeId,
                scope => scope.Id,
                (pattern, scope) => new
                {
                    scope.ChampionId,
                    scope.Position,
                    pattern.BuildId,
                    pattern.RunePageId,
                    pattern.Games,
                    pattern.Wins,
                })
            .Where(row => row.Position != string.Empty)
            .GroupBy(row => new { row.ChampionId, row.Position, row.BuildId, row.RunePageId })
            .Select(group => new
            {
                group.Key.ChampionId,
                group.Key.Position,
                group.Key.BuildId,
                group.Key.RunePageId,
                Games = group.Sum(row => row.Games),
                Wins = group.Sum(row => row.Wins),
            })
            .ToListAsync(ct);

        if (grouped.Count == 0)
        {
            return new Dictionary<(int, string), TopBuildReadModel>();
        }

        var buildIds = grouped.Select(row => row.BuildId).Distinct().ToList();
        var runeIds = grouped.Select(row => row.RunePageId).Distinct().ToList();

        var dimBuilds = await db.ChampionDimBuilds.AsNoTracking()
            .Where(dim => buildIds.Contains(dim.Id))
            .ToDictionaryAsync(dim => dim.Id, ct);
        var dimRunes = await db.ChampionDimRunePages.AsNoTracking()
            .Where(dim => runeIds.Contains(dim.Id))
            .ToDictionaryAsync(dim => dim.Id, ct);

        var result = new Dictionary<(int ChampionId, string Position), TopBuildReadModel>();

        foreach (var laneGroup in grouped.GroupBy(row => (row.ChampionId, row.Position)))
        {
            // Hydrate the bucket rows with their dim entries. Skip any row
            // whose dim lookup is missing (transient state during ingest) or
            // whose build / rune is malformed.
            var enriched = laneGroup
                .Select(row => new
                {
                    row.Games,
                    row.Wins,
                    Build = dimBuilds.GetValueOrDefault(row.BuildId),
                    Rune = dimRunes.GetValueOrDefault(row.RunePageId),
                })
                .Where(row => row.Build is not null && row.Rune is not null
                    && row.Build.BuildItem0 > 0 && row.Rune.PrimaryKeystoneId > 0)
                .ToList();

            if (enriched.Count == 0)
            {
                continue;
            }

            // Same first-tie ordering as ChampionBuildsQueryService: games
            // desc, firstItemId asc, keystoneId asc — so a champion's list
            // row and its detail page land on the same dominant bucket.
            var topBucket = enriched
                .GroupBy(row => (row.Build!.BuildItem0, row.Rune!.PrimaryKeystoneId))
                .Select(group => new
                {
                    FirstItem = group.Key.BuildItem0,
                    Keystone = group.Key.PrimaryKeystoneId,
                    Games = group.Sum(row => row.Games),
                    Wins = group.Sum(row => row.Wins),
                    Rows = group.ToList(),
                })
                .OrderByDescending(bucket => bucket.Games)
                .ThenBy(bucket => bucket.FirstItem)
                .ThenBy(bucket => bucket.Keystone)
                .First();

            // Consensus item path via the same tree-walk + threshold logic
            // the detail page uses for its core build path.
            var sequences = topBucket.Rows
                .Select(row => new ChampionBuildPathAnalyzer.BuildSequence(
                    row.Build!.BuildItem1, row.Build.BuildItem2, row.Build.BuildItem3,
                    row.Build.BuildItem4, row.Build.BuildItem5, row.Build.BuildItem6,
                    row.Games, row.Wins))
                .ToList();
            var tree = ChampionBuildPathAnalyzer.BuildItemTree(sequences, topBucket.Games);
            var (itemPath, _, _) = ChampionBuildPathAnalyzer.WalkPath(
                tree, topBucket.FirstItem, topBucket.Games, topBucket.Wins);

            // Dominant secondary tree within the top bucket.
            var secondaryStyleId = topBucket.Rows
                .GroupBy(row => row.Rune!.SecondaryStyleId)
                .OrderByDescending(group => group.Sum(row => row.Games))
                .ThenBy(group => group.Key)
                .First().Key;

            result[(laneGroup.Key.ChampionId, laneGroup.Key.Position)] = new TopBuildReadModel
            {
                FirstItemId = topBucket.FirstItem,
                PrimaryKeystoneId = topBucket.Keystone,
                SecondaryStyleId = secondaryStyleId,
                ItemPath = itemPath,
            };
        }

        return result;
    }
}
