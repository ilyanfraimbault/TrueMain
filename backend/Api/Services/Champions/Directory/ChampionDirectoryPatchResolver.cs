using System.Diagnostics;
using Core.Options;
using Data;
using Data.Aggregation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.Services.Champions.Scopes;

namespace TrueMain.Services.Champions.Directory;

/// <summary>
/// The first stage of the champion directory read: which patch it serves. A requested
/// patch is taken as-is; otherwise the newest patch that can actually fill a directory
/// (#1109), resolved once per aggregation version through <see cref="IChampionReadCache"/>.
/// </summary>
/// <remarks>
/// Built by <see cref="ChampionSummariesQueryService"/> on its own scoped dependencies,
/// and logs under its logger so every stage of one directory read stays on one surface.
/// </remarks>
internal sealed class ChampionDirectoryPatchResolver(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options,
    IOptions<ChampionsListOptions> championsOptions,
    IChampionReadCache cache,
    ILogger logger)
{
    // Keyed by the aggregation version like every other directory entry — see the
    // note on ChampionSummariesQueryService's cache keys.
    private const string ActivePatchCacheKey = "champions:summaries:active-patch";
    private const string PatchListCacheKey = "champions:summaries:patch-list";
    private const string Surface = ChampionSummariesQueryService.Surface;

    // How far back the servable walk looks before giving up and serving the newest
    // patch anyway. Bounded because the scan behind it costs one grouped pass per
    // patch and the table only grows: if the four newest patches are all too thin to
    // rank, the site has an ingestion problem that serving a fifth would only hide.
    private const int MaxServableWalkBack = 4;

    public async Task<string?> ResolveActivePatchAsync(string? requestedPatch, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(requestedPatch))
        {
            return requestedPatch;
        }

        return await cache.GetOrComputeAsync(ActivePatchCacheKey, ResolveActivePatchCoreAsync, ct);
    }

    private async Task<string?> ResolveActivePatchCoreAsync(CancellationToken ct)
    {
        var ordered = await LoadPatchesNewestFirstAsync(ct);
        return await ResolveServablePatchAsync(ordered, ct);
    }

    /// <summary>
    /// The newest patch that can actually fill a directory (#1109). Measures the
    /// candidates' lines past the floor and hands them to
    /// <see cref="ChampionAggregateScopeResolver.ResolveServablePatch"/>, which walks
    /// back from the newest until one clears
    /// <c>ChampionsList:MinServablePatchLines</c>.
    /// </summary>
    private async Task<string?> ResolveServablePatchAsync(
        IReadOnlyList<string> patchesNewestFirst, CancellationToken ct)
    {
        var minLines = championsOptions.Value.MinServablePatchLines;

        // Nothing to walk back to, or the bar is switched off: keep the pre-#1109
        // path exactly, including its lack of a second query.
        if (minLines <= 0 || patchesNewestFirst.Count <= 1)
        {
            return patchesNewestFirst.FirstOrDefault();
        }

        IReadOnlyList<string> candidates = [.. patchesNewestFirst.Take(MaxServableWalkBack)];
        var linesPastFloor = await LoadLinesPastFloorAsync(candidates, ct);

        var resolved = ChampionAggregateScopeResolver.ResolveServablePatch(candidates, linesPastFloor, minLines);

        // Patch day is the one time this line matters, and it is the one time someone
        // is looking: it says which patch the site is on, which one it declined, and
        // by how much — the three facts the "why is the tier list empty" question
        // needs. Information, not Warning: the fallback is the design working.
        if (!string.Equals(resolved, candidates.FirstOrDefault(), StringComparison.Ordinal))
        {
            logger.LogInformation(
                "{Surface} servable_patch resolved={Resolved} newest={Newest} newestLines={NewestLines} bar={Bar}",
                Surface,
                resolved,
                candidates.FirstOrDefault(),
                linesPastFloor.GetValueOrDefault(candidates.FirstOrDefault() ?? string.Empty),
                minLines);
        }

        return resolved;
    }

    /// <summary>
    /// Every patch the aggregate table holds for the queue, newest first. Cached on
    /// the active-patch TTL: the set only changes when a patch's first fold lands.
    /// </summary>
    private async Task<IReadOnlyList<string>> LoadPatchesNewestFirstAsync(CancellationToken ct)
    {
        return await cache.GetOrComputeAsync(PatchListCacheKey, LoadPatchesNewestFirstCoreAsync, ct);
    }

    private async Task<IReadOnlyList<string>> LoadPatchesNewestFirstCoreAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var distinctPatches = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.QueueId == (int)options.Value.QueueId)
            // Mains only, and deliberately not parameterised on the population —
            // for the same reason this resolution carries no elo clause: which
            // patch the site serves must not move when the reader changes a
            // filter. Pinning it to the default population also keeps the
            // servable-patch floor below honest once the non-main rows exist.
            .Where(scope => scope.IsMain)
            .Select(scope => scope.GameVersion)
            .Distinct()
            .ToListAsync(ct);
        sw.Stop();
        logger.LogInformation(
            "{Surface} sql=distinct_patches rows={Rows} elapsed={ElapsedMs}ms",
            Surface, distinctPatches.Count, sw.ElapsedMilliseconds);

        return ChampionAggregateScopeResolver.OrderNewestFirst(distinctPatches);
    }

    /// <summary>
    /// One grouped scan over the given patches, folded into the single counter the
    /// servable bar is measured against: how many <c>(champion, lane)</c> lines each
    /// patch has past <c>ChampionsList:MinSampleGames</c> — what the directory would
    /// actually render for it. Read only when the active-patch entry misses, once per
    /// its TTL, so it never lands on the hot path.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, int>> LoadLinesPastFloorAsync(
        IReadOnlyList<string> patches, CancellationToken ct)
    {
        if (patches.Count == 0)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        return await cache.GetOrComputeAsync(
            $"champions:summaries:lines-past-floor:{string.Join('|', patches)}",
            token => LoadLinesPastFloorCoreAsync(patches, token),
            ct);
    }

    private async Task<IReadOnlyDictionary<string, int>> LoadLinesPastFloorCoreAsync(
        IReadOnlyList<string> patches, CancellationToken ct)
    {
        // The same grouping the directory runs, minus the elo clause the resolution
        // must not have: switching bracket may not move the patch the site serves.
        // The population is pinned to mains for the same reason, and for a second
        // one: this is #1109's anti-thin-patch floor, and a patch that clears it
        // only on non-main volume would be served to the default, mains-only
        // directory with a truemain sample that is still too thin to show.
        var sw = Stopwatch.StartNew();
        var grouped = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.QueueId == (int)options.Value.QueueId)
            .Where(scope => patches.Contains(scope.GameVersion))
            .Where(scope => scope.IsMain)
            .GroupBy(scope => new { scope.GameVersion, scope.ChampionId, scope.Position })
            // Projected into an anonymous type and mapped after materialisation, the
            // same shape ComputeAllSummariesAsync uses: a grouped projection straight
            // into a struct's constructor is the kind of expression the provider is
            // free to refuse, and it would refuse it at runtime on the homepage.
            .Select(group => new
            {
                group.Key.GameVersion,
                group.Key.ChampionId,
                group.Key.Position,
                Games = group.Sum(scope => scope.Games)
            })
            .ToListAsync(ct);
        sw.Stop();
        logger.LogInformation(
            "{Surface} sql=lines_past_floor patches={Patches} rows={Rows} elapsed={ElapsedMs}ms",
            Surface, patches.Count, grouped.Count, sw.ElapsedMilliseconds);

        var rows = grouped
            .Select(row => new ChampionDirectoryLine(row.GameVersion, row.ChampionId, row.Position, row.Games))
            .ToList();

        var floor = championsOptions.Value.MinSampleGames;

        // Only the rows carrying a lane count — the same split ComputeAllSummariesAsync
        // makes between the patch total and the ranked rows. A patch with rows but no
        // line past the floor is simply absent here, which the resolver reads as zero.
        IReadOnlyDictionary<string, int> linesByPatch = ChampionDirectoryLines.Fold(rows)
            .Where(line => ChampionDirectoryLines.ClearsFloor(line, floor))
            .GroupBy(line => line.Patch, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        return linesByPatch;
    }
}
