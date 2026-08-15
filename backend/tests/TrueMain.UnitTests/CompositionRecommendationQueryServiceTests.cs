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
        var service = new CompositionRecommendationQueryService(
            matchQuery, buildQuery, new CountingGamesQueryService(), new StubLaneQueryService(), cache);

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
        var service = new CompositionRecommendationQueryService(
            matchQuery, buildQuery, new CountingGamesQueryService(), new StubLaneQueryService(), cache);

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
        var service = new CompositionRecommendationQueryService(
            matchQuery, buildQuery, new CountingGamesQueryService(), new StubLaneQueryService(), cache);

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

    [Fact]
    public async Task GetGamesAsync_ReusesTheSelectionTheRecommendationScanned()
    {
        var matchQuery = new CountingMatchQueryService { Matches = Refs(3) };
        var buildQuery = new CountingBuildQueryService();
        var gamesQuery = new CountingGamesQueryService();
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 16 });
        var service = new CompositionRecommendationQueryService(
            matchQuery, buildQuery, gamesQuery, new StubLaneQueryService(), cache);

        var criteria = new CompositionSearchCriteria
        {
            ChampionId = 157,
            Position = "MIDDLE",
            Enemies = new Dictionary<string, int> { ["MIDDLE"] = 238 },
        };

        await service.GetAsync(criteria, CancellationToken.None);
        var games = await service.GetGamesAsync(criteria, page: 1, pageSize: 0, CancellationToken.None);

        matchQuery.Calls.Should().Be(1, "opening the drawer must not re-scan match_participants");
        buildQuery.Calls.Should().Be(1, "the listing needs the selection, not the aggregation");
        games.Total.Should().Be(3);
        gamesQuery.Hydrations.Should().ContainSingle()
            .Which.Select(m => m.MatchId).Should().Equal("M0", "M1", "M2");
    }

    [Fact]
    public async Task GetGamesAsync_PagesTheSelectionInItsOwnOrder()
    {
        var matchQuery = new CountingMatchQueryService { Matches = Refs(5) };
        var gamesQuery = new CountingGamesQueryService();
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 16 });
        var service = new CompositionRecommendationQueryService(
            matchQuery, new CountingBuildQueryService(), gamesQuery, new StubLaneQueryService(), cache);

        var criteria = new CompositionSearchCriteria { ChampionId = 157, Position = "MIDDLE" };

        var second = await service.GetGamesAsync(criteria, page: 2, pageSize: 2, CancellationToken.None);

        second.Page.Should().Be(2);
        second.PageSize.Should().Be(2);
        second.Total.Should().Be(5);
        gamesQuery.Hydrations.Should().ContainSingle()
            .Which.Select(m => m.MatchId).Should().Equal("M2", "M3");
    }

    [Fact]
    public async Task GetGamesAsync_ClampsThePageSizeSoHydrationStaysBounded()
    {
        var matchQuery = new CountingMatchQueryService { Matches = Refs(100) };
        var gamesQuery = new CountingGamesQueryService();
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 16 });
        var service = new CompositionRecommendationQueryService(
            matchQuery, new CountingBuildQueryService(), gamesQuery, new StubLaneQueryService(), cache);

        var games = await service.GetGamesAsync(
            new CompositionSearchCriteria { ChampionId = 157, Position = "MIDDLE" },
            page: 0,
            pageSize: 500,
            CancellationToken.None);

        games.Page.Should().Be(1, "a non-positive page clamps to the first one");
        games.PageSize.Should().Be(25);
        gamesQuery.Hydrations.Should().ContainSingle().Which.Should().HaveCount(25);
    }

    private static IReadOnlyList<CompositionMatchRef> Refs(int count)
        => Enumerable.Range(0, count)
            .Select(i => new CompositionMatchRef
            {
                MatchId = $"M{i}",
                ParticipantId = 1,
                Score = count - i,
                Win = i % 2 == 0,
                GameStartTimeUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(-i),
                Puuid = $"puuid-{i}",
                IsTruemain = i == 0,
            })
            .ToList();

    private sealed class CountingMatchQueryService : ICompositionMatchQueryService
    {
        public int Calls { get; private set; }

        /// <summary>Games the fake selection returns, in selection order.</summary>
        public IReadOnlyList<CompositionMatchRef> Matches { get; init; } = [];

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
                TruemainGameCount = 0,
                MaxPossibleScore = 0,
                MeanSimilarity = 0,
                MatchupRequested = false,
                MatchupFound = true,
                Matches = Matches,
            });
        }
    }

    /// <summary>
    /// Records the refs it was handed instead of hitting the database, so the
    /// paging tests can assert on which slice of the selection was hydrated.
    /// </summary>
    /// <summary>
    /// Returns a fixed lane reading so the cache and plumbing tests stay about what
    /// they are about. The judging itself is covered against real Postgres, since it
    /// is a query over snapshots rather than arithmetic.
    /// </summary>
    private sealed class StubLaneQueryService : ICompositionLaneOutcomeQueryService
    {
        public int Calls { get; private set; }

        public Task<CompositionLaneReadModel> GetAsync(
            string position, IReadOnlyList<CompositionMatchRef> matches, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new CompositionLaneReadModel
            {
                MeasuredGames = matches.Count,
                DecidedGames = matches.Count,
                WinRate = matches.Count == 0 ? null : 0.5d,
                AverageGoldDiffAt15 = matches.Count == 0 ? null : 120d,
                AverageXpDiffAt15 = matches.Count == 0 ? null : -80d,
            });
        }
    }

    private sealed class CountingGamesQueryService : ICompositionGamesQueryService
    {
        public List<IReadOnlyList<CompositionMatchRef>> Hydrations { get; } = [];

        public Task<IReadOnlyList<CompositionGameReadModel>> HydrateAsync(
            IReadOnlyList<CompositionMatchRef> matches, CancellationToken ct)
        {
            Hydrations.Add(matches);
            return Task.FromResult<IReadOnlyList<CompositionGameReadModel>>([]);
        }
    }

    private sealed class CountingBuildQueryService : ICompositionBuildQueryService
    {
        public int Calls { get; private set; }

        public Task<CompositionBuildRecommendation> AggregateAsync(
            int championId,
            string position,
            IReadOnlyList<CompositionMatchRef> matches,
            int maxPossibleScore,
            CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new CompositionBuildRecommendation());
        }
    }
}
