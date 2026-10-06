using Core.Lol.Draft;
using Microsoft.Extensions.Caching.Memory;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Draft;
using TrueMain.Services.Champions.Scopes;
using TrueMain.Services.Champions.Synergies;

namespace TrueMain.UnitTests;

/// <summary>
/// The orchestration of <c>POST /champions/draft</c> (#1707): which candidates get
/// scored, what each reader is asked, how the readings reach the scorer and in what
/// order the picks come back. The scoring arithmetic itself is pinned by
/// <see cref="DraftScoringTests"/>; the readers are stubbed here.
/// </summary>
public sealed class DraftRecommendationQueryServiceTests
{
    private const int Self = 1;

    private readonly StubLanePriors priors = new();
    private readonly StubLanes lanes = new();
    private readonly StubSynergies synergies = new();
    private readonly StubEnemies enemies = new();
    private readonly DraftPatchScope scope = new("16.19", "16.18");

    private DraftRecommendationQueryService Service(IChampionReadCache? cache = null)
        => new(priors, new StubScopes(scope), lanes, synergies, enemies, cache ?? TestChampionReadCache.PassThrough());

    [Fact]
    public async Task UnavailableChampionsAreNeverScored()
    {
        var response = await Service().GetAsync(
            new DraftCriteria
            {
                Position = "MIDDLE",
                Candidates = [1, 2, 3, 4, 5, 6, 6],
                Bans = [2],
                EnemyChampions = [3],
                Allies = new Dictionary<string, int> { ["JUNGLE"] = 4 },
                HoveredAllies = new Dictionary<string, int> { ["UTILITY"] = 5 },
            },
            CancellationToken.None);

        Assert.Equal([1, 6], response.Candidates.Select(c => c.ChampionId).Order());
        Assert.Equal([1, 6], Assert.Single(lanes.RecordRequests).Order());
        Assert.Equal([1, 6], Assert.Single(enemies.Requests).Candidates.Order());
    }

    [Fact]
    public async Task NoEligibleCandidateStillPlacesTheEnemiesButReadsNothingElse()
    {
        var response = await Service().GetAsync(
            new DraftCriteria
            {
                Position = "MIDDLE",
                Candidates = [2],
                Bans = [2],
                EnemyChampions = [10],
                PinnedEnemyLanes = new Dictionary<int, string> { [10] = "TOP" },
            },
            CancellationToken.None);

        Assert.Empty(response.Candidates);
        Assert.Equal("TOP", Assert.Single(response.EnemyLanes).Position);
        Assert.Empty(lanes.RecordRequests);
        Assert.Empty(enemies.Requests);
        Assert.Empty(synergies.Requests);
    }

    [Fact]
    public async Task ThePinnedLaneOpponentCarriesTheMatchup()
    {
        lanes.Records[Self] = Record(versus: new() { [10] = new DraftComponent(0.05, 1_000) });

        var response = await Service().GetAsync(
            new DraftCriteria
            {
                Position = "middle",
                Candidates = [Self],
                EnemyChampions = [10, 11],
                PinnedEnemyLanes = new Dictionary<int, string> { [10] = "MIDDLE", [11] = "TOP" },
            },
            CancellationToken.None);

        Assert.Equal("MIDDLE", response.Position);
        Assert.Equal("16.19", response.Patch);
        Assert.Equal("16.18", response.PreviousPatch);
        Assert.Equal(10, response.LaneOpponentChampionId);
        Assert.Equal(1d, response.LaneOpponentConfidence);
        Assert.Equal(1d, response.LaneOpponentProbability, 9);
        var candidate = Assert.Single(response.Candidates);
        Assert.Equal(0.05, candidate.MatchupDelta, 9);
        Assert.Equal(1_000, candidate.MatchupGames);
        Assert.False(candidate.ThinSample);
    }

    [Fact]
    public async Task TheEnemyTeamTermWeighsOnlyTheEnemiesOffOurLane()
    {
        enemies.Pairings[Self] = new Dictionary<int, DraftComponent>
        {
            // Our lane opponent: the lane term carries it, the enemy-team term must not.
            [10] = new(-0.10, 500),
            [11] = new(0.04, 800),
        };

        var response = await Service().GetAsync(
            new DraftCriteria
            {
                Position = "MIDDLE",
                Candidates = [Self, 6],
                EnemyChampions = [10, 11],
                PinnedEnemyLanes = new Dictionary<int, string> { [10] = "MIDDLE", [11] = "TOP" },
                EloBracket = "GOLD_PLUS",
            },
            CancellationToken.None);

        var measured = response.Candidates.Single(c => c.ChampionId == Self);
        Assert.Equal(0.04, measured.EnemyDelta, 9);
        Assert.Equal(800, measured.EnemyGames);
        var unmeasured = response.Candidates.Single(c => c.ChampionId == 6);
        Assert.Equal(0, unmeasured.EnemyGames);

        var request = Assert.Single(enemies.Requests);
        Assert.Equal("MIDDLE", request.Position);
        Assert.Equal([10, 11], request.Enemies.Order());
        Assert.Equal("GOLD_PLUS", request.EloBracket);
        Assert.Equal(scope, request.Scope);
    }

    [Fact]
    public async Task PicksComeBackByScoreThenByChampionId()
    {
        lanes.LaneAverage = 0.5;
        lanes.Records[5] = Record(games: 1_000, wins: 600);
        lanes.Records[9] = Record(games: 1_000, wins: 400);

        var response = await Service().GetAsync(
            new DraftCriteria { Position = "MIDDLE", Candidates = [7, 9, 3, 5] },
            CancellationToken.None);

        // 3 and 7 have no reading at all: a tie at zero, broken by id.
        Assert.Equal([5, 3, 7, 9], response.Candidates.Select(c => c.ChampionId));
    }

    [Fact]
    public async Task TheBlindExpectationOnlyCountsOpponentsStillAvailable()
    {
        lanes.Shares[20] = 100;
        lanes.Shares[21] = 100;
        lanes.Shares[Self] = 100;
        lanes.Records[Self] = Record(versus: new()
        {
            [20] = new DraftComponent(0.02, 1_000),
            // Banned: nobody can play it into us.
            [21] = new DraftComponent(-0.30, 1_000),
            // The candidate itself: a champion is never its own opponent.
            [Self] = new DraftComponent(-0.30, 1_000),
        });

        var response = await Service().GetAsync(
            new DraftCriteria { Position = "MIDDLE", Candidates = [Self], Bans = [21] },
            CancellationToken.None);

        var blind = Assert.Single(response.Candidates).Blind;
        Assert.Equal(0.02, blind.Delta, 9);
        Assert.Equal(1_000, blind.Games);
        Assert.Equal(1, blind.LikelyOpponents);
    }

    [Fact]
    public async Task SynergyIsReadOncePerAllyOffOurLaneWithThePreviousPatchAsFallback()
    {
        const int candidate2 = 2;
        synergies.Partners[(64, "16.19")] = [Partner(Self, 0.03, 300)];
        synergies.Partners[(64, "16.18")] = [Partner(candidate2, 0.01, 200), Partner(Self, 0.50, 999)];
        synergies.Partners[(40, "16.19")] = [Partner(Self, 0.02, 100), Partner(candidate2, 0.02, 100)];

        var response = await Service().GetAsync(
            new DraftCriteria
            {
                Position = "MIDDLE",
                Candidates = [Self, candidate2],
                Allies = new Dictionary<string, int> { ["JUNGLE"] = 64, ["MIDDLE"] = 99 },
                // 77 hovers a lane already locked: the lock wins.
                HoveredAllies = new Dictionary<string, int> { ["utility"] = 40, ["JUNGLE"] = 77 },
                EloBracket = "EMERALD_PLUS",
            },
            CancellationToken.None);

        Assert.Equal(
            [(64, "JUNGLE", "16.19"), (64, "JUNGLE", "16.18"), (40, "UTILITY", "16.19")],
            synergies.Requests.Select(r => (r.Ally, r.AllyPosition, r.Patch)));
        Assert.All(synergies.Requests, r => Assert.Equal("MIDDLE", r.PartnerPosition));
        Assert.All(synergies.Requests, r => Assert.Equal("EMERALD_PLUS", r.EloBracket));

        // The hovered ally counts half (DraftScoringWeights.HoveredAlly); the current
        // patch's pairing is never overwritten by the previous one's.
        var first = response.Candidates.Single(c => c.ChampionId == Self);
        Assert.Equal(((1 * 0.03) + (0.5 * 0.02)) / 1.5, first.SynergyDelta, 9);
        Assert.Equal(400, first.SynergyGames);
        var second = response.Candidates.Single(c => c.ChampionId == candidate2);
        Assert.Equal(((1 * 0.01) + (0.5 * 0.02)) / 1.5, second.SynergyDelta, 9);
        Assert.Equal(300, second.SynergyGames);
    }

    [Fact]
    public async Task APickIsThinOnlyWhenBothItsLaneAndItsSynergyAreThin()
    {
        synergies.Partners[(64, "16.19")] = [Partner(Self, 0.03, DraftCandidateScorer.MinGames)];

        var response = await Service().GetAsync(
            new DraftCriteria
            {
                Position = "MIDDLE",
                Candidates = [Self, 2],
                Allies = new Dictionary<string, int> { ["JUNGLE"] = 64 },
            },
            CancellationToken.None);

        Assert.False(response.Candidates.Single(c => c.ChampionId == Self).ThinSample);
        Assert.True(response.Candidates.Single(c => c.ChampionId == 2).ThinSample);
    }

    [Fact]
    public async Task TheLanePriorIsPooledAcrossEloBrackets()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions { SizeLimit = 64 });
        var service = Service(TestChampionReadCache.Wrapping(memory));

        var gold = await service.GetAsync(Draft("GOLD_PLUS"), CancellationToken.None);
        var iron = await service.GetAsync(Draft("IRON"), CancellationToken.None);

        // One prior read serves every bracket (decisions/product-matchups.md) ...
        var request = Assert.Single(priors.Requests);
        Assert.Equal([10, 11], request.Champions);
        Assert.Equal("16.19", request.Patch);
        // ... while the rest of the response honours the requested one.
        Assert.NotEqual(gold.EloBracket, iron.EloBracket);
        Assert.Equal(["GOLD_PLUS", "IRON"], lanes.RecordBrackets);

        static DraftCriteria Draft(string bracket) => new()
        {
            Position = "MIDDLE",
            Candidates = [Self],
            EnemyChampions = [11, 10, 11],
            EloBracket = bracket,
        };
    }

    private static DraftLaneRecord Record(
        int games = 0,
        int wins = 0,
        Dictionary<int, DraftComponent>? versus = null)
        => new(
            versus is null ? games : Math.Max(games, versus.Values.Sum(v => v.Games)),
            wins,
            versus ?? new Dictionary<int, DraftComponent>(),
            new Dictionary<int, (int, int)>(),
            "16.19");

    private static ChampionSynergyEntry Partner(int championId, double synergy, int games)
        => new() { PartnerChampionId = championId, PartnerPosition = "MIDDLE", Synergy = synergy, Games = games };

    private sealed class StubScopes(DraftPatchScope scope) : IDraftPatchScopeResolver
    {
        public Task<DraftPatchScope> ResolveAsync(string? requestedPatch, CancellationToken ct)
            => Task.FromResult(scope);
    }

    private sealed class StubLanePriors : ILanePriorQueryService
    {
        public List<(List<int> Champions, string? Patch)> Requests { get; } = [];

        public Task<IReadOnlyDictionary<int, LanePrior>> GetAsync(
            IReadOnlyCollection<int> championIds,
            string? patch,
            CancellationToken ct)
        {
            Requests.Add((championIds.ToList(), patch));
            return Task.FromResult<IReadOnlyDictionary<int, LanePrior>>(new Dictionary<int, LanePrior>());
        }
    }

    private sealed class StubLanes : IDraftLaneReader
    {
        public Dictionary<int, DraftLaneRecord> Records { get; } = [];

        public Dictionary<int, double> Shares { get; } = [];

        public double LaneAverage { get; set; } = 0.5;

        public List<List<int>> RecordRequests { get; } = [];

        public List<string?> RecordBrackets { get; } = [];

        public Task<IReadOnlyDictionary<int, DraftLaneRecord>> ReadRecordsAsync(
            IReadOnlyCollection<int> championIds,
            string position,
            DraftPatchScope scope,
            string? eloBracket,
            CancellationToken ct)
        {
            RecordRequests.Add(championIds.ToList());
            RecordBrackets.Add(eloBracket);
            return Task.FromResult<IReadOnlyDictionary<int, DraftLaneRecord>>(
                Records.Where(r => championIds.Contains(r.Key)).ToDictionary(r => r.Key, r => r.Value));
        }

        public Task<IReadOnlyDictionary<int, double>> ReadLaneSharesAsync(
            string position,
            DraftPatchScope scope,
            string? eloBracket,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyDictionary<int, double>>(new Dictionary<int, double>(Shares));

        public Task<double> ReadLaneAverageAsync(
            string position,
            DraftPatchScope scope,
            string? eloBracket,
            CancellationToken ct)
            => Task.FromResult(LaneAverage);
    }

    private sealed class StubSynergies : IChampionSynergyQueryService
    {
        public Dictionary<(int Ally, string? Patch), List<ChampionSynergyEntry>> Partners { get; } = [];

        public List<(int Ally, string AllyPosition, string? Patch, string? PartnerPosition, string? EloBracket)> Requests { get; } = [];

        public Task<ChampionSynergiesResponse> GetSynergiesAsync(
            int championId,
            string position,
            string? patch,
            string? partnerPosition,
            string? eloBracket,
            CancellationToken ct)
        {
            Requests.Add((championId, position, patch, partnerPosition, eloBracket));
            return Task.FromResult(new ChampionSynergiesResponse
            {
                ChampionId = championId,
                Position = position,
                Patch = patch,
                Partners = Partners.GetValueOrDefault((championId, patch), []),
            });
        }

        public Task<ChampionTrioSynergiesResponse> GetTrioSynergiesAsync(
            int championId,
            string position,
            int partnerChampionId,
            string partnerPosition,
            string? patch,
            string? eloBracket,
            CancellationToken ct)
            => throw new NotSupportedException("The draft never reads trios.");
    }

    private sealed class StubEnemies : IDraftEnemyReader
    {
        public Dictionary<int, IReadOnlyDictionary<int, DraftComponent>> Pairings { get; } = [];

        public List<(List<int> Candidates, string Position, List<int> Enemies, DraftPatchScope Scope, string? EloBracket)> Requests { get; } = [];

        public Task<IReadOnlyDictionary<int, IReadOnlyDictionary<int, DraftComponent>>> ReadAsync(
            IReadOnlyCollection<int> candidates,
            string position,
            IReadOnlyCollection<int> enemies,
            DraftPatchScope scope,
            string? eloBracket,
            CancellationToken ct)
        {
            Requests.Add((candidates.ToList(), position, enemies.ToList(), scope, eloBracket));
            return Task.FromResult<IReadOnlyDictionary<int, IReadOnlyDictionary<int, DraftComponent>>>(
                new Dictionary<int, IReadOnlyDictionary<int, DraftComponent>>(Pairings));
        }
    }
}
