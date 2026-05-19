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
    /// Resolves the most-played <c>(BuildId, RunePageId)</c> tuple for every
    /// <c>(champion, position)</c> pair on <paramref name="activePatch"/>,
    /// then hydrates the matching dim rows. Single aggregate SQL query plus
    /// two dim lookups — independent of the directory row count.
    /// </summary>
    private async Task<IReadOnlyDictionary<(int ChampionId, string Position), TopBuildReadModel>> LoadTopBuildsAsync(
        string activePatch,
        CancellationToken ct)
    {
        var queueId = options.Value.QueueId;

        // Bring the (champion, position) onto each pattern row by joining
        // through the scope. The window-functioned `top-per-partition` is
        // done in memory after pulling the small aggregate set — there are
        // far fewer (champion, position, build, rune) combinations than raw
        // pattern rows, so this stays cheap even on a busy patch.
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
            })
            .ToListAsync(ct);

        if (grouped.Count == 0)
        {
            return new Dictionary<(int, string), TopBuildReadModel>();
        }

        var top = grouped
            .GroupBy(row => (row.ChampionId, row.Position))
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(row => row.Games)
                    .ThenBy(row => row.BuildId)
                    .ThenBy(row => row.RunePageId)
                    .First());

        var topBuildIds = top.Values.Select(row => row.BuildId).Distinct().ToList();
        var topRuneIds = top.Values.Select(row => row.RunePageId).Distinct().ToList();

        var dimBuilds = await db.ChampionDimBuilds.AsNoTracking()
            .Where(dim => topBuildIds.Contains(dim.Id))
            .ToDictionaryAsync(dim => dim.Id, ct);
        var dimRunes = await db.ChampionDimRunePages.AsNoTracking()
            .Where(dim => topRuneIds.Contains(dim.Id))
            .ToDictionaryAsync(dim => dim.Id, ct);

        var result = new Dictionary<(int ChampionId, string Position), TopBuildReadModel>(top.Count);
        foreach (var (key, row) in top)
        {
            if (!dimBuilds.TryGetValue(row.BuildId, out var build)) continue;
            if (!dimRunes.TryGetValue(row.RunePageId, out var rune)) continue;
            if (build.BuildItem0 <= 0 || rune.PrimaryKeystoneId <= 0) continue;

            var itemPath = new List<int>(7);
            void Append(int id) { if (id > 0) itemPath.Add(id); }
            Append(build.BuildItem0);
            Append(build.BuildItem1);
            Append(build.BuildItem2);
            Append(build.BuildItem3);
            Append(build.BuildItem4);
            Append(build.BuildItem5);
            Append(build.BuildItem6);

            result[key] = new TopBuildReadModel
            {
                FirstItemId = build.BuildItem0,
                PrimaryKeystoneId = rune.PrimaryKeystoneId,
                SecondaryStyleId = rune.SecondaryStyleId,
                ItemPath = itemPath,
            };
        }

        return result;
    }
}
