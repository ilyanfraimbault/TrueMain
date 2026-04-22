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
    public async Task<ChampionFoundationReadModel?> GetAsync(
        int championId,
        Guid? riotAccountId,
        string? patch,
        string? platformId,
        string? position,
        CancellationToken ct)
    {
        var scopes = await LoadScopedScopesAsync(championId, riotAccountId, patch, platformId, position, ct);
        if (scopes is null)
        {
            return null;
        }

        var scopeIds = scopes.Select(scope => scope.Id).ToList();
        var starterItems = await db.ChampionAggregateStarterItems.AsNoTracking()
            .Where(row => scopeIds.Contains(row.ScopeId))
            .ToListAsync(ct);
        var spellPairs = await db.ChampionAggregateSpellPairs.AsNoTracking()
            .Where(row => scopeIds.Contains(row.ScopeId))
            .ToListAsync(ct);
        var skillOrders = await db.ChampionAggregateSkillOrders.AsNoTracking()
            .Where(row => scopeIds.Contains(row.ScopeId))
            .ToListAsync(ct);
        var builds = await db.ChampionAggregateBuilds.AsNoTracking()
            .Where(row => scopeIds.Contains(row.ScopeId))
            .ToListAsync(ct);

        var sampleSize = scopes.Sum(scope => scope.Games);
        var advanced = ChampionOptionProjector.BuildAdvancedDetails(
            starterItems,
            spellPairs,
            skillOrders,
            sampleSize);

        return new ChampionFoundationReadModel
        {
            Summary = BuildSummary(championId, scopes.First().GameVersion, scopes),
            Core = ChampionCoreBuilder.Build(sampleSize, advanced, builds),
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
        var query = db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.ChampionId == championId && scope.QueueId == options.Value.QueueId);

        if (riotAccountId.HasValue)
        {
            query = query.Where(scope => scope.RiotAccountId == riotAccountId.Value);
        }

        if (!string.IsNullOrWhiteSpace(platformId))
        {
            query = query.Where(scope => scope.PlatformId == platformId);
        }

        if (!string.IsNullOrWhiteSpace(patch))
        {
            query = query.Where(scope => scope.GameVersion == patch);
        }

        if (!string.IsNullOrWhiteSpace(position))
        {
            query = query.Where(scope => scope.Position == position);
        }

        var scopes = await query.ToListAsync(ct);
        if (scopes.Count == 0)
        {
            return null;
        }

        var selectedPatch = ChampionAggregateScopeResolver.ResolvePatchVersion(scopes, patch);
        if (string.IsNullOrWhiteSpace(selectedPatch))
        {
            return null;
        }

        var patchScopes = scopes
            .Where(scope => string.Equals(scope.GameVersion, selectedPatch, StringComparison.Ordinal))
            .ToList();
        if (patchScopes.Count == 0)
        {
            return null;
        }

        var effectivePosition = string.IsNullOrWhiteSpace(position)
            ? ChampionAggregateScopeResolver.ResolveDominantPosition(patchScopes)
            : position;

        var scopedScopes = string.IsNullOrWhiteSpace(effectivePosition)
            ? patchScopes
            : patchScopes
                .Where(scope => string.Equals(scope.Position, effectivePosition, StringComparison.Ordinal))
                .ToList();

        return scopedScopes.Count == 0 ? null : scopedScopes;
    }

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
