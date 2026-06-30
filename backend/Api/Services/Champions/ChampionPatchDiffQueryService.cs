using Core.Lol.Patches;
using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Builds the per-champion patch diff (issue #534) by reading the same
/// per-patch aggregates the build view serves. It resolves the position and the
/// two patches to compare, then leans on <see cref="IChampionBuildsQueryService"/>
/// to materialise each side so the diff and the full build view never disagree
/// — the dominant first item / keystone / skill order shown here is exactly the
/// top build tab on each patch. The cross-patch work (resolving available
/// patches, picking defaults, computing deltas) lives here; the per-patch
/// aggregation does not.
/// </summary>
public sealed class ChampionPatchDiffQueryService(
    TrueMainDbContext db,
    IChampionBuildsQueryService buildsQueryService,
    IOptions<MainAnalysisOptions> options)
    : IChampionPatchDiffQueryService
{
    public async Task<ChampionPatchDiffReadModel> GetDiffAsync(
        int championId,
        string? fromPatch,
        string? toPatch,
        string? position,
        CancellationToken ct)
    {
        // Cast the configured queue to the int the persisted scope stores,
        // matching the other champion services so we read the same rows.
        var queueId = (int)options.Value.QueueId;

        // One round-trip for every positioned scope of this champion: the row
        // set is a handful of patches × at most five lanes, so folding the
        // position / patch resolution in memory beats re-querying. Blank
        // positions are the "no lane" sentinel and never anchor a per-lane diff.
        var rows = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.ChampionId == championId && scope.QueueId == queueId)
            .Where(scope => scope.Position.Trim() != string.Empty)
            .GroupBy(scope => new { scope.GameVersion, scope.Position })
            .Select(group => new ScopeRow(group.Key.GameVersion, group.Key.Position, group.Sum(s => s.Games)))
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return new ChampionPatchDiffReadModel { ChampionId = championId };
        }

        var resolvedPosition = ResolvePosition(rows, position);
        if (string.IsNullOrEmpty(resolvedPosition))
        {
            return new ChampionPatchDiffReadModel { ChampionId = championId };
        }

        // Patches that actually have data for this (champion, position), newest
        // → oldest. The selectors on the page are populated from this set, and
        // the from/to defaults are the two newest so the page opens on the most
        // recent patch-over-patch change.
        var lanePatches = rows
            .Where(row => string.Equals(row.Position, resolvedPosition, StringComparison.Ordinal))
            .Select(row => row.Patch)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(ParsePatch)
            .ToList();

        if (lanePatches.Count == 0)
        {
            return new ChampionPatchDiffReadModel
            {
                ChampionId = championId,
                Position = resolvedPosition,
            };
        }

        var (resolvedFrom, resolvedTo) = ResolvePatches(lanePatches, fromPatch, toPatch);

        // Pull both sides from the build service so the diff agrees with the
        // build view tab-for-tab. Each call resolves its own slice on the
        // resolved position; a patch with no data comes back null → null side.
        var fromSide = await BuildSideAsync(championId, resolvedFrom, resolvedPosition, ct);
        var toSide = await BuildSideAsync(championId, resolvedTo, resolvedPosition, ct);

        return new ChampionPatchDiffReadModel
        {
            ChampionId = championId,
            Position = resolvedPosition,
            From = fromSide,
            To = toSide,
            Delta = BuildDelta(fromSide, toSide),
        };
    }

    private async Task<ChampionPatchDiffSide?> BuildSideAsync(
        int championId,
        string? patch,
        string position,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(patch))
        {
            return null;
        }

        var response = await buildsQueryService.GetAsync(championId, patch, position, ct);
        if (response is null || response.TotalGames == 0)
        {
            return null;
        }

        // The top build tab is the most-played (first item, keystone) slice —
        // GetAsync orders builds by games descending, so the head is the
        // dominant build. Its core skill order is the dominant sequence.
        var topBuild = response.Builds.Count > 0 ? response.Builds[0] : null;

        return new ChampionPatchDiffSide
        {
            Patch = response.Patch,
            Games = response.TotalGames,
            Wins = response.TotalWins,
            WinRate = response.TotalGames == 0
                ? 0
                : (double)response.TotalWins / response.TotalGames,
            TopFirstItemId = topBuild?.FirstItemId ?? 0,
            TopKeystoneId = topBuild?.PrimaryKeystoneId ?? 0,
            TopSkillOrder = topBuild?.Core.SkillOrder?.Sequence ?? [],
        };
    }

    private static ChampionPatchDiffDelta? BuildDelta(
        ChampionPatchDiffSide? from,
        ChampionPatchDiffSide? to)
    {
        if (from is null || to is null)
        {
            return null;
        }

        return new ChampionPatchDiffDelta
        {
            WinRateChange = to.WinRate - from.WinRate,
            // Treat a missing (zero) first item / keystone as "not changed":
            // a side with no qualifying build can't claim a build shift, so we
            // only flag a change when both sides actually have a top build.
            FirstItemChanged = from.TopFirstItemId != 0
                && to.TopFirstItemId != 0
                && from.TopFirstItemId != to.TopFirstItemId,
            KeystoneChanged = from.TopKeystoneId != 0
                && to.TopKeystoneId != 0
                && from.TopKeystoneId != to.TopKeystoneId,
            SkillOrderChanged = from.TopSkillOrder.Count > 0
                && to.TopSkillOrder.Count > 0
                && !from.TopSkillOrder.SequenceEqual(to.TopSkillOrder, StringComparer.Ordinal),
        };
    }

    private static string ResolvePosition(IReadOnlyList<ScopeRow> rows, string? requestedPosition)
    {
        if (!string.IsNullOrWhiteSpace(requestedPosition))
        {
            // Honour the page filter even if the champion has no rows there —
            // an empty diff is the correct answer for an off-meta lane.
            return requestedPosition;
        }

        // Default lane = the champion's most-played position on its latest
        // patch with data, so the diff opens on the same slice the detail page
        // and trend chart land on.
        var latestPatch = rows
            .Select(row => row.Patch)
            .OrderByDescending(ParsePatch)
            .First();

        return ChampionAggregateScopeResolver.ResolveDominantPosition(
            rows
                .Where(row => string.Equals(row.Patch, latestPatch, StringComparison.Ordinal))
                .Select(row => (row.Position, row.Games)));
    }

    /// <summary>
    /// Resolves the (from, to) patch pair. <paramref name="lanePatches"/> is
    /// ordered newest → oldest. Explicit requests are honoured as-is (an
    /// off-data patch simply yields a null side); when a side is unspecified we
    /// default to → newest and from → the patch immediately before it. The pair
    /// is normalised so <c>from</c> is always the older of the two — the diff
    /// reads "what changed going into the newer patch".
    /// </summary>
    private static (string? From, string? To) ResolvePatches(
        IReadOnlyList<string> lanePatches,
        string? requestedFrom,
        string? requestedTo)
    {
        var normalizedFrom = NormalizeRequested(requestedFrom);
        var normalizedTo = NormalizeRequested(requestedTo);

        var to = normalizedTo ?? lanePatches[0];
        var from = normalizedFrom
            ?? lanePatches.FirstOrDefault(patch => !string.Equals(patch, to, StringComparison.Ordinal))
            // Only one patch of data: compare it against itself so the page can
            // still render both sides (the delta is then a flat zero).
            ?? to;

        // Keep from older than to regardless of how the caller passed them, so
        // the win-rate delta always reads as the newer patch minus the older.
        return ParsePatch(from).CompareTo(ParsePatch(to)) > 0 ? (to, from) : (from, to);
    }

    private static string? NormalizeRequested(string? patch)
    {
        if (string.IsNullOrWhiteSpace(patch))
        {
            return null;
        }

        var normalized = PatchVersion.Normalize(patch);
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static (int Major, int Minor) ParsePatch(string gameVersion)
        => PatchVersion.TryParse(gameVersion, out var version)
            ? (version.Major, version.Minor)
            : (0, 0);

    private sealed record ScopeRow(string Patch, string Position, int Games);
}
