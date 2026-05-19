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
        var cacheKey = $"champions:summaries:{patch ?? "latest"}";
        if (cache.TryGetValue<IReadOnlyList<ChampionSummaryReadModel>>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var summaries = await ComputeAllSummariesAsync(patch, ct);
        cache.Set(cacheKey, summaries, SummariesCacheTtl);
        return summaries;
    }

    private async Task<IReadOnlyList<ChampionSummaryReadModel>> ComputeAllSummariesAsync(string? requestedPatch, CancellationToken ct)
    {
        // Resolve the active patch first so the row pull below can apply the
        // GameVersion filter in SQL. Loading every aggregate row for the queue
        // before filtering in-memory would re-scan the whole table on every
        // cache miss — fine when only the latest patch matters but wasteful
        // once historical patches are reachable through ?patch=.
        var activePatch = requestedPatch;
        if (string.IsNullOrEmpty(activePatch))
        {
            var distinctPatches = await db.ChampionAggregateScopes
                .AsNoTracking()
                .Where(scope => scope.QueueId == options.Value.QueueId)
                .Select(scope => scope.GameVersion)
                .Distinct()
                .ToListAsync(ct);
            activePatch = ChampionAggregateScopeResolver.ResolvePatchVersion(distinctPatches, requestedPatch: null);
        }
        if (string.IsNullOrEmpty(activePatch))
        {
            return [];
        }

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

        // Pick rates are computed straight from match_participants, not from
        // the TrueMain-scoped aggregates. The aggregate-based ratio is biased
        // (it only sees games played by accounts the ingestor flagged as
        // mains), whereas a pick rate is a meta signal and needs to reflect
        // every observed game on the patch.
        var pickRates = await LoadPickRatesAsync(activePatch, ct);

        // Pre-computed denominator for LanePlayRate (role distribution within
        // this champion's TrueMain games). PickRate uses a separate
        // participant-based denominator from `pickRates` above.
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

                pickRates.TryGetValue((group.Key.ChampionId, group.Key.Position), out var pickStats);
                topBuilds.TryGetValue((group.Key.ChampionId, group.Key.Position), out var topBuild);
                return new ChampionSummaryReadModel
                {
                    ChampionId = group.Key.ChampionId,
                    Games = games,
                    Wins = wins,
                    WinRate = games == 0 ? 0 : (double)wins / games,
                    PickRate = pickStats.LaneTotal == 0 ? 0 : (double)pickStats.Picks / pickStats.LaneTotal,
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
    /// Counts <see cref="Data.Entities.MatchParticipant"/> rows per
    /// <c>(champion, position)</c> and per <c>position</c> for the active
    /// patch and queue, then returns the picks + lane-total pair for each
    /// <c>(champion, position)</c>. Pick rate consumers divide the two.
    ///
    /// Pulling from <c>match_participants</c> rather than the TrueMain-scoped
    /// aggregates removes the TrueMain selection bias from the metric — pick
    /// rate is a meta signal, so the denominator is every game on the patch,
    /// not just the games we have aggregate rows for.
    /// </summary>
    private async Task<IReadOnlyDictionary<(int ChampionId, string Position), (long Picks, long LaneTotal)>> LoadPickRatesAsync(
        string activePatch,
        CancellationToken ct)
    {
        var queueId = options.Value.QueueId;
        // Match.GameVersion stores Riot's raw four-segment string
        // (e.g. "16.4.521.1234"); the aggregate scopes carry the normalized
        // two-segment form ("16.4"). Both forms point at the same patch — we
        // accept either equality or a LIKE prefix so historic rows with the
        // short form still match.
        var patchPrefix = $"{activePatch}.%";

        var picks = await db.MatchParticipants
            .AsNoTracking()
            .Where(participant => participant.TeamPosition != string.Empty)
            .Join(
                db.Matches.AsNoTracking()
                    .Where(match => match.QueueId == queueId
                        && (match.GameVersion == activePatch
                            || EF.Functions.Like(match.GameVersion, patchPrefix))),
                participant => participant.MatchId,
                match => match.Id,
                (participant, _) => new { participant.ChampionId, participant.TeamPosition })
            .GroupBy(row => new { row.ChampionId, row.TeamPosition })
            .Select(group => new
            {
                group.Key.ChampionId,
                group.Key.TeamPosition,
                Picks = (long)group.Count(),
            })
            .ToListAsync(ct);

        if (picks.Count == 0)
        {
            return new Dictionary<(int, string), (long, long)>();
        }

        var laneTotals = picks
            .GroupBy(row => row.TeamPosition, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(row => row.Picks),
                StringComparer.Ordinal);

        return picks.ToDictionary(
            row => (row.ChampionId, row.TeamPosition),
            row => (row.Picks, laneTotals.GetValueOrDefault(row.TeamPosition, 0L)));
    }

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

            if (enriched.Count == 0) continue;

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
