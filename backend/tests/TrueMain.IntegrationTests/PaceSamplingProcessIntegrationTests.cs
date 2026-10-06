using AwesomeAssertions;
using Core.Lol.Identifiers;
using Core.Lol.Map;
using Core.Lol.Ranking;
using Data.Entities;
using Data.Ops.Mongo;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Summaries;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.TestKit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The low-tier pace sampler (#1912) against a real database: what it folds, what it never
/// writes, how it avoids counting a match twice, and the caps it spends under.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class PaceSamplingProcessIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime NowUtc = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
    private const int GameMinutes = 20;

    private readonly PostgresFixture _fixture;

    public PaceSamplingProcessIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Run_FoldsTheSeedsGamesAtTheirTier_AndStoresNoMatch()
    {
        // Three seeds: two share a game, the third played a remake.
        var riot = new FakeRiot();
        riot.Games["seed-1"] = ["EUW1_1"];
        riot.Games["seed-2"] = ["EUW1_1"];
        riot.Games["seed-3"] = ["EUW1_2"];
        riot.Durations["EUW1_2"] = 200;

        var summary = (await RunAsync(riot, Options(maxPerRun: 9))).Should().BeOfType<PaceSamplingSummary>().Subject;

        // Page + three id lists + EUW1_1 (match + timeline) + EUW1_2 (match only: a remake
        // is not worth its timeline), whatever order the seeds are drawn in. Then fewer than a
        // page and a game are left.
        summary.RiotCalls.Should().Be(7);
        summary.MatchesSampled.Should().Be(1);
        summary.MatchesAlreadyKnown.Should().Be(1);
        summary.MatchesUnusable.Should().Be(1);
        summary.Tiers.Should().ContainSingle().Which.Should().Be(new PaceSamplingTierSummary("GOLD", 1, 10 * GameMinutes));
        riot.TimelineCalls.Should().Equal("EUW1_1");

        await using var db = _fixture.CreateDbContext();
        var stats = await db.PaceBenchmarkStats.AsNoTracking().ToListAsync();
        stats.Sum(stat => stat.Count).Should().Be(10 * GameMinutes * 2);
        stats.Should().OnlyContain(stat => stat.Tier == RankTier.Gold && stat.Patch == "16.19");
        (await db.PaceSampledMatches.Select(match => match.MatchId).ToListAsync()).Should().BeEquivalentTo(["EUW1_1", "EUW1_2"]);
        (await db.Matches.AnyAsync()).Should().BeFalse();
        (await db.MatchParticipants.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Run_NeverCountsAMatchTwice_AcrossRunsOrWithTheRegularIngestion()
    {
        var riot = new FakeRiot();
        riot.Games["seed-1"] = ["EUW1_1"];
        riot.Games["seed-2"] = ["EUW1_9"];
        riot.Games["seed-3"] = [];
        await SeedStoredMatchAsync("EUW1_9");

        await RunAsync(riot, Options(maxPerRun: 8));
        var second = (await RunAsync(riot, Options(maxPerRun: 8))).Should().BeOfType<PaceSamplingSummary>().Subject;

        second.MatchesSampled.Should().Be(0);
        second.MatchesAlreadyKnown.Should().BeGreaterThanOrEqualTo(2);
        riot.TimelineCalls.Should().Equal("EUW1_1");

        await using var db = _fixture.CreateDbContext();
        (await db.PaceBenchmarkStats.SumAsync(stat => stat.Count)).Should().Be(10 * GameMinutes * 2);
    }

    [Fact]
    public async Task Run_SpendsOnlyWhatTheDayLeft()
    {
        var riot = new FakeRiot();
        riot.Games["seed-1"] = ["EUW1_1"];
        var runs = new FakeProcessRunStore();
        runs.Runs.Add(new ProcessRunDocument
        {
            ProcessName = "PaceSampling",
            StartedAtUtc = NowUtc.AddHours(-2),
            SummaryJson = """{"riotCalls":97}""",
        });

        var summary = await RunAsync(riot, Options(maxPerRun: 50, maxPerDay: 100), runs);

        summary.Should().BeOfType<SkippedSummary>();
        riot.LadderCalls.Should().Be(0);
    }

    [Fact]
    public async Task Run_IsANoOp_WithoutABudget()
    {
        var riot = new FakeRiot();

        var summary = await RunAsync(riot, Options(maxPerRun: 0));

        summary.Should().BeOfType<NoWorkSummary>();
        riot.LadderCalls.Should().Be(0);
    }

    private static PaceSamplingOptions Options(int maxPerRun, int maxPerDay = 0) => new()
    {
        Platforms = ["EUW1"],
        TierScope = ["Gold"],
        MaxRequestsPerRun = maxPerRun,
        MaxRequestsPerDay = maxPerDay,
        SeedsPerPage = 3,
        MatchesPerSeed = 1,
    };

    private async Task<IProcessRunSummary?> RunAsync(FakeRiot riot, PaceSamplingOptions options, FakeProcessRunStore? runs = null)
    {
        var process = new PaceSamplingProcess(
            NullLogger<PaceSamplingProcess>.Instance,
            riot,
            riot,
            _fixture.CreateSessionFactory(),
            runs ?? new FakeProcessRunStore(),
            new FixedTimeProvider(NowUtc),
            MsOptions.Create(options));

        return await process.RunCoreAsync(CancellationToken.None);
    }

    private async Task SeedStoredMatchAsync(string matchId)
    {
        await using var db = _fixture.CreateDbContext();
        db.Matches.Add(new Match
        {
            Id = matchId,
            PlatformId = "EUW1",
            QueueId = (int)LolQueueId.RankedSoloDuo,
            MapId = (int)LolMapId.SummonersRift,
            GameStartTimeUtc = NowUtc.AddDays(-1),
            GameDurationSeconds = 1500,
            GameVersion = "16.19.1",
            CreatedAtUtc = NowUtc.AddDays(-1),
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// One Gold ladder page of three seeds whatever the division or page, each seed's games as
    /// <see cref="Games"/> says, and every game a 20-minute ranked game with ten laners.
    /// </summary>
    private sealed class FakeRiot : IRiotPlatformClient, IRiotMatchClient
    {
        public Dictionary<string, List<string>> Games { get; } = [];
        public Dictionary<string, int> Durations { get; } = [];
        public List<string> TimelineCalls { get; } = [];
        public int LadderCalls { get; private set; }

        public Task<List<RiotLeagueDivisionEntryDto>> GetLeagueEntriesAsync(
            PlatformRoute platform, string queue, string tier, string division, int page, CancellationToken ct)
        {
            LadderCalls++;
            return Task.FromResult(Enumerable.Range(1, 3)
                .Select(i => new RiotLeagueDivisionEntryDto { Puuid = $"seed-{i}", Tier = "GOLD", Rank = division })
                .ToList());
        }

        public Task<List<string>> GetMatchIdsAsync(MatchIdQuery query, CancellationToken ct)
            => Task.FromResult(Games.GetValueOrDefault(query.Puuid) ?? []);

        public Task<RiotMatchDto> GetMatchAsync(string matchId, RegionalRoute region, CancellationToken ct)
            => Task.FromResult(new RiotMatchDto
            {
                Metadata = new RiotMatchMetadataDto { MatchId = matchId },
                Info = new RiotMatchInfoDto
                {
                    QueueId = 420,
                    GameDuration = Durations.GetValueOrDefault(matchId, GameMinutes * 60 + 30),
                    GameVersion = "16.19.700.1234",
                    Participants = Enumerable.Range(1, 10)
                        .Select(id => new RiotParticipantDto { ParticipantId = id, TeamPosition = LanePositions.All[(id - 1) % 5] })
                        .ToList(),
                },
            });

        public Task<MatchTimelineDto> GetTimelineAsync(string matchId, RegionalRoute region, CancellationToken ct)
        {
            TimelineCalls.Add(matchId);
            return Task.FromResult(new MatchTimelineDto
            {
                Frames = Enumerable.Range(0, GameMinutes + 1)
                    .Select(minute => new MatchTimelineFrameDto
                    {
                        TimestampMs = minute * 60_000 + 10,
                        ParticipantFrames = Enumerable.Range(1, 10)
                            .Select(id => new MatchParticipantFrameDto { ParticipantId = id, MinionsKilled = minute * 6, TotalGold = 500 + minute * 350 })
                            .ToList(),
                    })
                    .ToList(),
            });
        }

        public Task<RiotLeagueListDto> GetChallengerLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct) => throw new NotSupportedException();
        public Task<RiotLeagueListDto> GetGrandmasterLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct) => throw new NotSupportedException();
        public Task<RiotLeagueListDto> GetMasterLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct) => throw new NotSupportedException();
        public Task<RiotSummonerDto> GetSummonerAsync(PlatformRoute platform, string summonerId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RiotSummonerDto> GetSummonerByPuuidAsync(PlatformRoute platform, string puuid, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<RiotChampionMasteryDto>> GetChampionMasteriesAsync(PlatformRoute platform, string puuid, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<RiotLeagueEntryByPuuidDto>> GetLeagueEntriesByPuuidAsync(PlatformRoute platform, string puuid, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTime nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(nowUtc);
    }
}
