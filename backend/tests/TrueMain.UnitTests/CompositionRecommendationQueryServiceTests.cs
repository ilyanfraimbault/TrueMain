using AwesomeAssertions;
using Microsoft.Extensions.Caching.Memory;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions;

namespace TrueMain.UnitTests;

public sealed class CompositionRecommendationQueryServiceTests
{
    [Fact]
    public async Task GetAsync_IdenticalCriteria_HitTheCacheInsteadOfRescanning()
    {
        var matchQuery = new CountingMatchQueryService();
        var buildQuery = new CountingBuildQueryService();
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 16 });
        var service = new CompositionRecommendationQueryService(matchQuery, buildQuery, cache);

        var criteria = new CompositionSearchCriteria
        {
            ChampionId = 157,
            Position = "MIDDLE",
            Enemies = new Dictionary<string, int> { ["MIDDLE"] = 238 },
        };

        var first = await service.GetAsync(criteria, CancellationToken.None);
        var second = await service.GetAsync(criteria, CancellationToken.None);

        matchQuery.Calls.Should().Be(1, "the second identical request must be served from the cache");
        buildQuery.Calls.Should().Be(1);
        second.Should().BeSameAs(first);
    }

    [Fact]
    public async Task GetAsync_DifferentDraft_MissesTheCache()
    {
        var matchQuery = new CountingMatchQueryService();
        var buildQuery = new CountingBuildQueryService();
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 16 });
        var service = new CompositionRecommendationQueryService(matchQuery, buildQuery, cache);

        await service.GetAsync(
            new CompositionSearchCriteria
            {
                ChampionId = 157,
                Position = "MIDDLE",
                Enemies = new Dictionary<string, int> { ["MIDDLE"] = 238 },
            },
            CancellationToken.None);
        await service.GetAsync(
            new CompositionSearchCriteria
            {
                ChampionId = 157,
                Position = "MIDDLE",
                Enemies = new Dictionary<string, int> { ["MIDDLE"] = 91 },
            },
            CancellationToken.None);

        matchQuery.Calls.Should().Be(2, "a different draft is a different cache key");
    }

    [Fact]
    public async Task GetAsync_SlotOrder_DoesNotChangeTheCacheKey()
    {
        var matchQuery = new CountingMatchQueryService();
        var buildQuery = new CountingBuildQueryService();
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 16 });
        var service = new CompositionRecommendationQueryService(matchQuery, buildQuery, cache);

        // Same draft, slots listed in a different order: dictionaries are
        // sorted into the key, so the second call must hit.
        await service.GetAsync(
            new CompositionSearchCriteria
            {
                ChampionId = 157,
                Position = "MIDDLE",
                Enemies = new Dictionary<string, int> { ["MIDDLE"] = 238, ["TOP"] = 266 },
            },
            CancellationToken.None);
        await service.GetAsync(
            new CompositionSearchCriteria
            {
                ChampionId = 157,
                Position = "MIDDLE",
                Enemies = new Dictionary<string, int> { ["TOP"] = 266, ["MIDDLE"] = 238 },
            },
            CancellationToken.None);

        matchQuery.Calls.Should().Be(1);
    }

    private sealed class CountingMatchQueryService : ICompositionMatchQueryService
    {
        public int Calls { get; private set; }

        public Task<CompositionMatchesResult> FindTopMatchesAsync(
            CompositionSearchCriteria criteria, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new CompositionMatchesResult
            {
                ChampionId = criteria.ChampionId,
                Position = criteria.Position,
                Patch = null,
                CandidatePoolSize = 0,
                MaxPossibleScore = 0,
                MeanSimilarity = 0,
                Matches = [],
            });
        }
    }

    private sealed class CountingBuildQueryService : ICompositionBuildQueryService
    {
        public int Calls { get; private set; }

        public Task<CompositionBuildRecommendation> AggregateAsync(
            int championId,
            string position,
            IReadOnlyList<CompositionMatchRef> matches,
            CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new CompositionBuildRecommendation());
        }
    }
}
