using Core.Lol.Ranking;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Orchestrates the two composition stages — top-K similarity selection
/// (<see cref="ICompositionMatchQueryService"/>) then win-weighted build
/// aggregation (<see cref="ICompositionBuildQueryService"/>) — behind the shared
/// champion read cache, keyed on the normalised request. The cache is load-bearing,
/// not an optimisation: the selection scan is live over match_participants and
/// single-threaded in prod, so repeated identical drafts (the common case
/// while a lobby theorycrafts) must not re-scan — and the single flight behind the
/// same door means a lobby refreshing together shares one scan rather than one each.
///
/// The selection is cached separately from the response so the provenance
/// drawer (#940) — which pages that very same selection back to the user —
/// reads it instead of re-scanning, and so the recommendation payload stays
/// free of the hundred match rows nobody asked for.
/// </summary>
public sealed class CompositionRecommendationQueryService(
    ICompositionMatchQueryService matchQueryService,
    ICompositionBuildQueryService buildQueryService,
    ICompositionGamesQueryService gamesQueryService,
    ICompositionLaneOutcomeQueryService laneQueryService,
    IChampionReadCache cache)
    : ICompositionRecommendationQueryService
{
    /// <summary>Page size of the provenance listing when the caller passes none.</summary>
    private const int DefaultGamesPageSize = 10;

    /// <summary>
    /// Ceiling on the provenance page size: hydration grades every participant
    /// of every listed match, so the page size is what bounds its cost.
    /// </summary>
    private const int MaxGamesPageSize = 25;

    public Task<CompositionBuildResponse> GetAsync(
        CompositionSearchCriteria criteria,
        CancellationToken ct)
    {
        var bracketToken = EloBracket.ResolveToken(criteria.EloBracket);

        return cache.GetOrComputeAsync(
            BuildCacheKey(criteria, bracketToken),
            token => ComputeAsync(criteria, bracketToken, token),
            ct);
    }

    private async Task<CompositionBuildResponse> ComputeAsync(
        CompositionSearchCriteria criteria,
        string bracketToken,
        CancellationToken ct)
    {
        var matches = await GetMatchesAsync(criteria, bracketToken, ct);
        var build = await buildQueryService.AggregateAsync(
            criteria.ChampionId, criteria.Position, matches.Matches, matches.MaxPossibleScore, ct);
        // Judged over the selection itself, so every cell of the tool's stat line
        // counts the same games (#1117).
        var lane = await laneQueryService.GetAsync(criteria.Position, matches.Matches, ct);

        var response = new CompositionBuildResponse
        {
            ChampionId = criteria.ChampionId,
            Position = criteria.Position,
            Patch = matches.Patch,
            EloBracket = bracketToken,
            MatchupRequested = matches.MatchupRequested,
            MatchupFound = matches.MatchupFound,
            Confidence = new CompositionConfidenceReadModel
            {
                SampleSize = build.GamesConsidered,
                CandidatePoolSize = matches.CandidatePoolSize,
                TruemainGameCount = matches.TruemainGameCount,
                MaxPossibleScore = matches.MaxPossibleScore,
                MeanSimilarity = matches.MeanSimilarity,
            },
            Lane = lane,
            Build = build,
        };

        return response;
    }

    public async Task<CompositionBuildGamesResponse> GetGamesAsync(
        CompositionSearchCriteria criteria,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var bracketToken = EloBracket.ResolveToken(criteria.EloBracket);
        var matches = await GetMatchesAsync(criteria, bracketToken, ct);

        var clampedPageSize = pageSize <= 0
            ? DefaultGamesPageSize
            : Math.Min(pageSize, MaxGamesPageSize);
        var clampedPage = page < 1 ? 1 : page;

        // The selection order IS the answer here — mains first, then
        // similarity, recency breaking ties — so the page is a plain slice of
        // it, never re-sorted.
        var slice = matches.Matches
            .Skip((clampedPage - 1) * clampedPageSize)
            .Take(clampedPageSize)
            .ToList();

        return new CompositionBuildGamesResponse
        {
            ChampionId = criteria.ChampionId,
            Position = criteria.Position,
            Patch = matches.Patch,
            Page = clampedPage,
            PageSize = clampedPageSize,
            Total = matches.Matches.Count,
            MaxPossibleScore = matches.MaxPossibleScore,
            Games = await gamesQueryService.HydrateAsync(slice, ct),
        };
    }

    /// <summary>
    /// The selection stage behind its own cache entry, so a recommendation and
    /// its provenance listing scan match_participants once between them.
    /// </summary>
    private Task<CompositionMatchesResult> GetMatchesAsync(
        CompositionSearchCriteria criteria,
        string bracketToken,
        CancellationToken ct)
        => cache.GetOrComputeAsync(
            BuildCacheKey(criteria, bracketToken) + ":matches",
            token => matchQueryService.FindTopMatchesAsync(criteria, token),
            ct);

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
