using Core.Lol.Patches;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.Services.Champions;

/// <summary>
/// Two-pass loader for the set of <see cref="ChampionAggregateScope"/>
/// rows that match a champion + patch + position request. First pass
/// resolves the dominant patch/position when callers leave them
/// unspecified, second pass re-queries with the resolved values pinned in
/// SQL via <c>WhereChampionScope</c>. Shared by every service that needs
/// to project pattern data for a single champion view.
/// </summary>
internal static class ChampionScopeLoader
{
    public static async Task<IReadOnlyList<ChampionAggregateScope>?> LoadAsync(
        TrueMainDbContext db,
        int queueId,
        int championId,
        string? patch,
        string? position,
        CancellationToken ct)
    {
        var normalizedPatch = string.IsNullOrWhiteSpace(patch)
            ? null
            : PatchVersion.Normalize(patch);

        var baseQuery = db.ChampionAggregateScopes
            .AsNoTracking()
            .WhereChampionScope(championId, queueId, riotAccountId: null, normalizedPatch, platformId: null, position);

        var resolutionRows = await baseQuery
            .Select(scope => new ScopeResolutionRow(scope.GameVersion, scope.Position, scope.Games))
            .ToListAsync(ct);
        if (resolutionRows.Count == 0)
        {
            return null;
        }

        string? selectedPatch;
        if (!string.IsNullOrEmpty(normalizedPatch))
        {
            // Caller pinned a specific patch — respect it.
            selectedPatch = ChampionAggregateScopeResolver.ResolvePatchVersion(
                resolutionRows.Select(row => row.GameVersion),
                normalizedPatch);
        }
        else
        {
            // No patch requested: default to the global latest patch across
            // every champion on the active queue. Champions that don't yet
            // have data on the global latest fall back to their own most
            // recent patch, so the page never lands on an empty slice.
            var globalLatestPatch = await ResolveGlobalLatestPatchAsync(db, queueId, ct);
            var hasGlobalLatest = !string.IsNullOrEmpty(globalLatestPatch)
                && resolutionRows.Any(row => string.Equals(row.GameVersion, globalLatestPatch, StringComparison.Ordinal));
            selectedPatch = hasGlobalLatest
                ? globalLatestPatch
                : ChampionAggregateScopeResolver.ResolvePatchVersion(
                    resolutionRows.Select(row => row.GameVersion),
                    requestedPatch: null);
        }
        if (string.IsNullOrWhiteSpace(selectedPatch))
        {
            return null;
        }

        var effectivePosition = string.IsNullOrWhiteSpace(position)
            ? ChampionAggregateScopeResolver.ResolveDominantPosition(
                resolutionRows
                    .Where(row => string.Equals(row.GameVersion, selectedPatch, StringComparison.Ordinal))
                    .Select(row => (row.Position, row.Games)))
            : position;

        var scopedScopes = await db.ChampionAggregateScopes
            .AsNoTracking()
            .WhereChampionScope(
                championId,
                queueId,
                riotAccountId: null,
                selectedPatch,
                platformId: null,
                string.IsNullOrWhiteSpace(effectivePosition) ? null : effectivePosition)
            .ToListAsync(ct);

        return scopedScopes.Count == 0 ? null : scopedScopes;
    }

    private sealed record ScopeResolutionRow(string GameVersion, string Position, int Games);

    private static async Task<string?> ResolveGlobalLatestPatchAsync(
        TrueMainDbContext db,
        int queueId,
        CancellationToken ct)
    {
        // SELECT DISTINCT GameVersion ... pulls a tiny set (one row per
        // ingested patch) so this stays cheap even without caching. Routes
        // through the existing resolver so the same parsing/ordering rules
        // apply.
        var versions = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.QueueId == queueId)
            .Select(scope => scope.GameVersion)
            .Distinct()
            .ToListAsync(ct);
        return ChampionAggregateScopeResolver.ResolvePatchVersion(versions, requestedPatch: null);
    }
}
