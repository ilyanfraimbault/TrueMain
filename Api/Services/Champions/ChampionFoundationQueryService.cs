using Core.Options;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public sealed class ChampionFoundationQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options) : IChampionFoundationQueryService
{
    public Task<ChampionFoundationReadModel?> GetAsync(
        int championId,
        Guid? riotAccountId,
        string? patch,
        string? platformId,
        string? position,
        CancellationToken ct)
        => GetAsync(championId, riotAccountId, patch, platformId, position, ChampionPatternPivot.None, ct);

    public async Task<ChampionFoundationReadModel?> GetAsync(
        int championId,
        Guid? riotAccountId,
        string? patch,
        string? platformId,
        string? position,
        ChampionPatternPivot pivot,
        CancellationToken ct)
    {
        var scopes = await LoadScopedScopesAsync(championId, riotAccountId, patch, platformId, position, ct);
        if (scopes is null)
        {
            return null;
        }

        var scopeIds = scopes.Select(scope => scope.Id).ToList();
        var projection = await ChampionPatternProjector.ProjectAsync(db, scopeIds, pivot, ct);

        // SampleSize must reflect the (possibly filtered) pattern volume so
        // play-rates compute against the right denominator. With no pivot
        // this collapses to the legacy scope-level total; with a build
        // pivot it shrinks to the games that played that build, which is
        // the correct denominator for "given build X, how often does this
        // rune page show up".
        var sampleSize = projection.Builds.Sum(build => build.Games);
        if (sampleSize == 0)
        {
            sampleSize = scopes.Sum(scope => scope.Games);
        }

        var advanced = ChampionOptionProjector.BuildAdvancedDetails(
            projection.StarterItems,
            projection.SpellPairs,
            projection.SkillOrders,
            projection.RunePages,
            sampleSize);

        return new ChampionFoundationReadModel
        {
            Summary = BuildSummary(championId, scopes.First().GameVersion, scopes),
            Core = ChampionCoreBuilder.Build(sampleSize, advanced, projection.Builds),
            Advanced = advanced
        };
    }

    internal async Task<IReadOnlyList<ChampionAggregateScope>?> LoadScopedScopesAsync(
        int championId,
        Guid? riotAccountId,
        string? patch,
        string? platformId,
        string? position,
        CancellationToken ct)
    {
        // Pass 1: a light projection over the (champion, queue, account,
        // patch, platform, position) slice. We only need the columns that
        // feed the dominant-patch and dominant-position resolution; pulling
        // entities would also drag the RiotAccount navigation surface for
        // no reason. The strict equality filters (championId, queueId,
        // riotAccountId, platformId, and patch/position when supplied)
        // already live in WhereChampionScope and translate to SQL.
        var baseQuery = db.ChampionAggregateScopes
            .AsNoTracking()
            .WhereChampionScope(championId, options.Value.QueueId, riotAccountId, patch, platformId, position);

        var resolutionRows = await baseQuery
            .Select(scope => new ScopeResolutionRow(scope.GameVersion, scope.Position, scope.Games))
            .ToListAsync(ct);
        if (resolutionRows.Count == 0)
        {
            return null;
        }

        var selectedPatch = ChampionAggregateScopeResolver.ResolvePatchVersion(
            resolutionRows.Select(row => row.GameVersion).ToList(),
            patch);
        if (string.IsNullOrWhiteSpace(selectedPatch))
        {
            return null;
        }

        var effectivePosition = string.IsNullOrWhiteSpace(position)
            ? ChampionAggregateScopeResolver.ResolveDominantPosition(
                resolutionRows
                    .Where(row => string.Equals(row.GameVersion, selectedPatch, StringComparison.Ordinal))
                    .Select(row => (row.Position, row.Games))
                    .ToList())
            : position;

        // Pass 2: re-query with the resolved patch (and position when
        // available) pushed into SQL via WhereChampionScope. This keeps
        // the materialisation tight even when the caller didn't pin a
        // patch upfront.
        var scopedScopes = await db.ChampionAggregateScopes
            .AsNoTracking()
            .WhereChampionScope(
                championId,
                options.Value.QueueId,
                riotAccountId,
                selectedPatch,
                platformId,
                string.IsNullOrWhiteSpace(effectivePosition) ? null : effectivePosition)
            .ToListAsync(ct);

        return scopedScopes.Count == 0 ? null : scopedScopes;
    }

    private sealed record ScopeResolutionRow(string GameVersion, string Position, int Games);

    private static ChampionSummaryReadModel BuildSummary(
        int championId,
        string latestPatchVersion,
        IReadOnlyCollection<ChampionAggregateScope> scopes)
    {
        var totalGames = scopes.Sum(scope => scope.Games);
        var totalWins = scopes.Sum(scope => scope.Wins);
        var trueMainCount = scopes.Select(scope => scope.RiotAccountId).Distinct().Count();
        var dominantPosition = ChampionAggregateScopeResolver.ResolveDominantPosition(scopes);

        return new ChampionSummaryReadModel
        {
            ChampionId = championId,
            Games = totalGames,
            WinRate = ChampionOptionProjector.ComputeRate(totalWins, totalGames),
            TrueMainCount = trueMainCount,
            Position = dominantPosition,
            LatestPatchVersion = latestPatchVersion,
            LastUpdatedAtUtc = scopes.Max(scope => scope.AggregatedAtUtc)
        };
    }
}
