using Core.Lol.Map;
using Core.Lol.Patches;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.Services.Champions.Scopes;

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
///
/// <para>
/// <see cref="LanePositions.AllPositions"/> as the position skips the dominant-position
/// pick and keeps every position's scopes at the resolved patch, so the caller folds
/// the champion across all of them (the player champion page's "All positions").
/// </para>
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
        IReadOnlyCollection<string>? eloBrackets = null,
        bool truemainsOnly = true,
        int defaultPatchMinGames = 0)
    {
        var normalizedPatch = string.IsNullOrWhiteSpace(patch)
            ? null
            : PatchVersion.Normalize(patch);

        var allPositions = string.Equals(position, LanePositions.AllPositions, StringComparison.Ordinal);
        var requestedPosition = allPositions ? null : position;

        var baseQuery = db.ChampionAggregateScopes
            .AsNoTracking()
            .WithBuilds(db)
            .WhereChampionScope(
                championId, queueId, riotAccountId, normalizedPatch, platformId, requestedPosition,
                // The resolution pass has to stand on the same population as the
                // pinned pass below: resolving the dominant patch over mains and
                // then reading it over everyone (or the reverse) can land on a
                // patch the second pass holds nothing for.
                truemainsOnly: truemainsOnly);

        var resolutionRows = await baseQuery
            .Select(scope => new ScopeResolutionRow(scope.GameVersion, scope.Position, scope.Games))
            .ToListAsync(ct);
        if (resolutionRows.Count == 0)
        {
            return null;
        }

        // An explicit patch still wins over every default below.
        var selectedPatch = !string.IsNullOrWhiteSpace(normalizedPatch)
            ? normalizedPatch
            : ResolveDefaultPatch(resolutionRows, allPositions, minGames, defaultPatchMinGames);
        if (string.IsNullOrWhiteSpace(selectedPatch))
        {
            return null;
        }

        var effectivePosition = string.IsNullOrWhiteSpace(position)
            ? ChampionAggregateScopeResolver.ResolveDominantPosition(
                resolutionRows
                    .Where(row => string.Equals(row.GameVersion, selectedPatch, StringComparison.Ordinal))
                    .Select(row => (row.Position, row.Games)))
            : requestedPosition;

        // Patch / position are resolved bracket-agnostically above (the first
        // pass passes no brackets) so switching bracket never shifts the
        // rendered patch + position. The bracket filter is applied only here,
        // on the final scope slice.
        var scopedScopes = await db.ChampionAggregateScopes
            .AsNoTracking()
            .WithBuilds(db)
            .WhereChampionScope(
                championId,
                queueId,
                riotAccountId,
                selectedPatch,
                platformId,
                string.IsNullOrWhiteSpace(effectivePosition) ? null : effectivePosition,
                eloBrackets,
                truemainsOnly)
            .ToListAsync(ct);

        return scopedScopes.Count == 0 ? null : scopedScopes;
    }

    /// <summary>
    /// The patch a request without one is served on. An explicit patch never reaches here.
    /// </summary>
    private static string? ResolveDefaultPatch(
        IReadOnlyList<ScopeResolutionRow> resolutionRows,
        bool allPositions,
        int? minGames,
        int defaultPatchMinGames)
    {
        // Across every position the floor is on the patch's whole total, not on its
        // busiest position: that total is the slice the page will render.
        var rows = resolutionRows
            .Select(row => (row.GameVersion, allPositions ? LanePositions.AllPositions : row.Position, row.Games))
            .ToList();

        // Player-scoped views (minGames set) default to the most recent patch
        // where the player actually has enough games, not the global latest — a
        // champion main whose newest patch is thin would otherwise render a
        // sparse slice. This is a *preference*, not a gate: when no patch clears
        // the floor we still fall back to the latest patch that has any games so
        // a champion the player has genuinely played renders a (thin) build
        // rather than 404-ing.
        if (minGames is { } floor)
        {
            return ChampionAggregateScopeResolver.ResolveLatestPatchAboveFloor(rows, floor)
                ?? ChampionAggregateScopeResolver.ResolvePatchVersion(
                    resolutionRows.Select(row => row.GameVersion), null);
        }

        // The public page (#2249): a patch released hours ago holds a few dozen games
        // per lane and prunes the build tree to a stub, while the previous one sits
        // beside it settled. The site-wide servable bar (#1109) cannot see that — it
        // clears on forty champions, not on this one. Resolved over the same population
        // as the slice and bracket-agnostically, like the rest of this pass.
        return ChampionAggregateScopeResolver.ResolveFilledPatch(rows, defaultPatchMinGames);
    }

    private sealed record ScopeResolutionRow(string GameVersion, string Position, int Games);
}
