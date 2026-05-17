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
    /// <summary>
    /// Loads the scope rows for a champion + patch + position. When
    /// <paramref name="patch"/> is null/empty and
    /// <paramref name="globalLatestPatch"/> is provided, the champion lands
    /// on the global latest patch if it has data there, otherwise falls
    /// back to its own most recent patch. Caller owns the global-latest
    /// lookup so it can cache or short-circuit it.
    /// </summary>
    public static async Task<IReadOnlyList<ChampionAggregateScope>?> LoadAsync(
        TrueMainDbContext db,
        int queueId,
        int championId,
        string? patch,
        string? position,
        CancellationToken ct,
        string? globalLatestPatch = null)
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
            // Caller pinned a specific patch — respect it. ResolvePatchVersion
            // canonicalises the input (e.g. "16.4.521" → "16.4") and ignores
            // the available game versions when one is requested, so a pinned
            // patch with no data flows through to the second-pass query and
            // returns null (→ 404) rather than silently falling back.
            selectedPatch = ChampionAggregateScopeResolver.ResolvePatchVersion(
                resolutionRows.Select(row => row.GameVersion),
                normalizedPatch);
        }
        else
        {
            // No patch requested: prefer the global latest patch supplied by
            // the caller so the page lands on the current meta even if this
            // champion's data trails by a release. Falls back to the
            // champion-specific latest when there's no data on the global
            // latest yet, so we never strand the page on an empty slice.
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
}
