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
///
/// When a <c>riotAccountId</c> is supplied the loader narrows the
/// scope to a single player (their games on that champion) instead of the
/// global pool — every other step (patch / position resolution, SQL pinning)
/// is identical, because the aggregate schema already stores one scope row
/// per (account, champion, patch, platform, queue, position).
/// </summary>
internal static class ChampionScopeLoader
{
    public static async Task<IReadOnlyList<ChampionAggregateScope>?> LoadAsync(
        TrueMainDbContext db,
        int queueId,
        int championId,
        string? patch,
        string? position,
        CancellationToken ct,
        Guid? riotAccountId = null,
        string? platformId = null,
        int? minGames = null,
        IReadOnlyCollection<string>? eloBrackets = null)
    {
        var normalizedPatch = string.IsNullOrWhiteSpace(patch)
            ? null
            : PatchVersion.Normalize(patch);

        var baseQuery = db.ChampionAggregateScopes
            .AsNoTracking()
            .WhereChampionScope(championId, queueId, riotAccountId, normalizedPatch, platformId, position);

        var resolutionRows = await baseQuery
            .Select(scope => new ScopeResolutionRow(scope.GameVersion, scope.Position, scope.Games))
            .ToListAsync(ct);
        if (resolutionRows.Count == 0)
        {
            return null;
        }

        // Player-scoped views (minGames set) default to the most recent patch
        // where the player actually has enough games, not the global latest — a
        // champion main whose newest patch is thin would otherwise render an
        // empty "not enough games" state. An explicit patch request still wins.
        var selectedPatch = normalizedPatch is null && minGames is { } floor
            ? ChampionAggregateScopeResolver.ResolveLatestPatchAboveFloor(
                resolutionRows.Select(row => (row.GameVersion, row.Position, row.Games)),
                floor)
            : ChampionAggregateScopeResolver.ResolvePatchVersion(
                resolutionRows.Select(row => row.GameVersion),
                normalizedPatch);
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

        // Patch / position are resolved bracket-agnostically above (the first
        // pass passes no bracket) so switching bracket never shifts the
        // rendered patch + position. The bracket filter is applied only here,
        // on the final scope slice.
        var scopedScopes = await db.ChampionAggregateScopes
            .AsNoTracking()
            .WhereChampionScope(
                championId,
                queueId,
                riotAccountId,
                selectedPatch,
                platformId,
                string.IsNullOrWhiteSpace(effectivePosition) ? null : effectivePosition,
                eloBrackets)
            .ToListAsync(ct);

        return scopedScopes.Count == 0 ? null : scopedScopes;
    }

    private sealed record ScopeResolutionRow(string GameVersion, string Position, int Games);
}
