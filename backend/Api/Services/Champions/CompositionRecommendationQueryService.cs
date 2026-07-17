using Core.Lol.Ranking;
using Microsoft.Extensions.Caching.Memory;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Orchestrates the two composition stages — top-K similarity selection
/// (<see cref="ICompositionMatchQueryService"/>) then win-weighted build
/// aggregation (<see cref="ICompositionBuildQueryService"/>) — behind a short
/// in-memory cache keyed on the normalised request. The cache is load-bearing,
/// not an optimisation: the selection scan is live over match_participants and
/// single-threaded in prod, so repeated identical drafts (the common case
/// while a lobby theorycrafts) must not re-scan.
/// </summary>
public sealed class CompositionRecommendationQueryService(
    ICompositionMatchQueryService matchQueryService,
    ICompositionBuildQueryService buildQueryService,
    IMemoryCache cache)
    : ICompositionRecommendationQueryService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public async Task<CompositionBuildResponse> GetAsync(
        CompositionSearchCriteria criteria,
        CancellationToken ct)
    {
        var bracketToken = EloBracket.ResolveToken(criteria.EloBracket);
        var cacheKey = BuildCacheKey(criteria, bracketToken);
        if (cache.TryGetValue<CompositionBuildResponse>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var matches = await matchQueryService.FindTopMatchesAsync(criteria, ct);
        var build = await buildQueryService.AggregateAsync(
            criteria.ChampionId, criteria.Position, matches.Matches, ct);

        var response = new CompositionBuildResponse
        {
            ChampionId = criteria.ChampionId,
            Position = criteria.Position,
            Patch = matches.Patch,
            EloBracket = bracketToken,
            Confidence = new CompositionConfidenceReadModel
            {
                SampleSize = build.GamesConsidered,
                CandidatePoolSize = matches.CandidatePoolSize,
                MaxPossibleScore = matches.MaxPossibleScore,
                MeanSimilarity = matches.MeanSimilarity,
            },
            Build = build,
        };

        cache.Set(cacheKey, response, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
            Size = 1,
        });

        return response;
    }

    /// <summary>
    /// Deterministic key over the normalised criteria: slots are sorted by
    /// position so the same draft always hits the same entry regardless of the
    /// order the caller listed the picks in.
    /// </summary>
    private static string BuildCacheKey(CompositionSearchCriteria criteria, string bracketToken)
    {
        static string Slots(IReadOnlyDictionary<string, int> slots)
            => string.Join(
                ',',
                slots
                    .OrderBy(s => s.Key, StringComparer.Ordinal)
                    .Select(s => $"{s.Key}={s.Value}"));

        return "champions:composition-build:"
            + $"{criteria.ChampionId}:{criteria.Position}:{criteria.Patch ?? "all"}:{bracketToken}:"
            + $"A[{Slots(criteria.Allies)}]:E[{Slots(criteria.Enemies)}]";
    }
}
