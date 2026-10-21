using AwesomeAssertions;
using Core.Lol.Map;
using Core.Lol.Ranking;
using Core.Options;
using Data;
using Data.Aggregation;
using Data.Entities;
using Ingestor.Options;
using Ingestor.Processes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Progression;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The scaling chart moved from a live fold over <c>match_participants</c> to the
/// <c>champion_scaling_stats</c> aggregate (#1368). This pins that the move changed nothing a
/// reader can see: on one seeded corpus — two platforms, two patches, mains and non-mains,
/// untracked seats, remakes by duration and by Riot's flag, another queue, unstamped and
/// stamped elo bands, durations on every bucket edge — the aggregate-backed read answers
/// exactly what the live SQL it replaced answered, cell by cell.
///
/// <para>
/// The live SQL is kept here, verbatim from the service it was deleted from, as the oracle:
/// it is the definition the aggregate has to reproduce, and nothing in production runs it.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChampionScalingAggregationParityIntegrationTests : IAsyncLifetime
{
    private const int QueueId = 420;
    private const int OtherQueueId = 440;
    private const int MinGames = 2;

    private static readonly string[] Platforms = ["KR", "EUW1"];
    private static readonly string[] Versions = ["16.4.521.123", "16.5.530.1", "16.04.7"];
    private static readonly string[] Patches = ["16.4", "16.5"];
    private static readonly int[] ChampionPool = [157, 238, 222, 412, 64, 86, 122, 60, 81, 350, 103, 266];
    private static readonly string[] Lanes = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];
    private static readonly string[] StoredBands = ["", "GOLD", "PLATINUM", "EMERALD", "DIAMOND", "MASTER"];
    private static readonly string?[] BracketFilters = [null, "GOLD", "EMERALD_PLUS", "MASTER"];
    private static readonly int[] EdgeDurations = [299, 300, 1199, 1200, 1499, 1500, 1799, 1800, 2099, 2100];

    private readonly PostgresFixture _fixture;

    public ChampionScalingAggregationParityIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task TheAggregateAnswersWhatTheLiveFoldAnswered_ForEveryCell()
    {
        await SeedCorpusAsync(new Random(1368), matches: 600);

        // Small batches on purpose: the parity has to survive a match landing in any
        // batch, and the drain has to stop on the short last one.
        await CreateProcess(batchSize: 37).RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        var service = new ChampionScalingQueryService(
            db,
            Microsoft.Extensions.Options.Options.Create(new ChampionsListOptions { MinMatchupGames = MinGames }),
            TestChampionReadCache.PassThrough());

        var cells = 0;
        var populated = 0;

        foreach (var championId in ChampionPool)
        {
            foreach (var lane in Lanes)
            {
                foreach (var patch in Patches.Cast<string?>().Prepend(null))
                {
                    foreach (var bracket in BracketFilters)
                    {
                        var expected = await LiveScalingAsync(db, championId, lane, patch, bracket);
                        var actual = await service.GetAsync(championId, lane, patch, bracket, CancellationToken.None);

                        actual.Buckets.Select(b => (b.Bucket, b.Label, b.Games, b.WinRate))
                            .Should().Equal(
                                expected.Buckets.Select(b => (b.Bucket, b.Label, b.Games, b.WinRate)),
                                $"champion {championId} {lane}, patch {patch ?? "all"}, bracket {bracket ?? "all"}");
                        actual.ScalingIndex.Should().Be(expected.ScalingIndex);

                        cells++;
                        populated += expected.Buckets.Count > 0 ? 1 : 0;
                    }
                }
            }
        }

        // A parity over empty answers would prove nothing: the corpus has to put real
        // games behind a good share of the cells.
        cells.Should().Be(ChampionPool.Length * Lanes.Length * (Patches.Length + 1) * BracketFilters.Length);
        populated.Should().BeGreaterThan(cells / 3);
    }

    [Fact]
    public async Task ARerunFoldsNothingTwice_AndEveryMatchOfTheQueueIsFlagged()
    {
        await SeedCorpusAsync(new Random(7), matches: 120);

        var process = CreateProcess(batchSize: 50);
        await process.RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        var gamesAfterFirstRun = await db.ChampionScalingStats.AsNoTracking().SumAsync(s => s.Games);

        await process.RunCoreAsync(CancellationToken.None);

        (await db.ChampionScalingStats.AsNoTracking().SumAsync(s => s.Games)).Should().Be(gamesAfterFirstRun);
        gamesAfterFirstRun.Should().BePositive();
        (await db.Matches.CountAsync(m => m.QueueId == QueueId && !m.ScalingAggregated))
            .Should().Be(0, "a remake or a match with no main is still flagged, or it is re-read forever");
        (await db.Matches.CountAsync(m => m.QueueId == OtherQueueId && m.ScalingAggregated))
            .Should().Be(0, "the fold only drains the analysed queue");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1199, 0)]
    [InlineData(1200, 1)]
    [InlineData(1499, 1)]
    [InlineData(1500, 2)]
    [InlineData(1800, 3)]
    [InlineData(2099, 3)]
    [InlineData(2100, 4)]
    [InlineData(5000, 4)]
    public void TheBucketEdgesAreTheOnesTheLiveFoldUsed(int durationSeconds, int bucket)
        => ChampionScalingBuckets.Of(durationSeconds).Should().Be(bucket);

    /// <summary>
    /// The read <c>ChampionScalingQueryService</c> ran before #1368, kept as the oracle:
    /// the cohort composed live, joined to the cohort's games, grouped by the duration
    /// CASE, floored per bucket.
    /// </summary>
    private static async Task<ChampionScalingResponse> LiveScalingAsync(
        TrueMainDbContext db,
        int championId,
        string position,
        string? patch,
        string? eloBracket)
    {
        string[] bucketLabels = ["<20m", "20-25m", "25-30m", "30-35m", "35m+"];
        // The cells already pass a normalised patch, which is what PatchFilter hands the read.
        var normalizedPatch = patch;
        var bands = EloBracket.ResolveFilterOrEmpty(eloBracket);

        var participants = ChampionCohort.Members(db, QueueId, normalizedPatch)
            .Where(p => p.ChampionId == championId && p.TeamPosition == position);
        if (bands is not null)
        {
            participants = participants.Where(p => bands.Contains(p.EloBracket));
        }

        var rows = await participants
            .Join(
                ChampionCohort.Games(db, QueueId, normalizedPatch),
                participant => participant.MatchId,
                match => match.Id,
                (participant, match) => new { participant.Win, match.GameDurationSeconds })
            .GroupBy(x => x.GameDurationSeconds < 1200 ? 0
                : x.GameDurationSeconds < 1500 ? 1
                : x.GameDurationSeconds < 1800 ? 2
                : x.GameDurationSeconds < 2100 ? 3
                : 4)
            .Select(g => new { Bucket = g.Key, Games = g.Count(), Wins = g.Sum(x => x.Win ? 1 : 0) })
            .Where(x => x.Games >= MinGames)
            .ToListAsync();

        var buckets = rows
            .OrderBy(x => x.Bucket)
            .Select(x => new ChampionScalingBucket
            {
                Bucket = x.Bucket,
                Label = bucketLabels[x.Bucket],
                Games = x.Games,
                WinRate = (double)x.Wins / x.Games
            })
            .ToList();

        return new ChampionScalingResponse
        {
            ChampionId = championId,
            Position = position,
            Patch = normalizedPatch,
            Buckets = buckets,
            ScalingIndex = buckets.Count >= 2 ? buckets[^1].WinRate - buckets[0].WinRate : null
        };
    }

    private ChampionScalingAggregationProcess CreateProcess(int batchSize)
        => new(
            NullLogger<ChampionScalingAggregationProcess>.Instance,
            Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions { QueueId = LolQueueId.RankedSoloDuo }),
            Microsoft.Extensions.Options.Options.Create(
                new ScalingAggregationOptions { MatchBatchSize = batchSize, MaxMatchesPerRun = 0 }),
            new TestDbContextFactory(_fixture),
            TimeProvider.System);

    /// <summary>
    /// Thirty tracked players over two platforms, each a main of four champions of the pool
    /// and a tracked non-main of a fifth, and <paramref name="matches"/> ten-seat games where
    /// every seat is one of them about half the time and an untracked stranger otherwise.
    /// </summary>
    private async Task SeedCorpusAsync(Random random, int matches)
    {
        await using var db = _fixture.CreateDbContext();

        var players = new List<(string Platform, string Puuid, Guid AccountId)>();
        for (var i = 0; i < 30; i++)
        {
            var platform = Platforms[i % Platforms.Length];
            var puuid = $"scaling-player-{i}";
            var account = new RiotAccountBuilder()
                .WithPlatformId(platform)
                .WithPuuid(puuid)
                .WithGameName($"ScalingPlayer{i}")
                .WithTagLine("TEST")
                .Build();
            db.RiotAccounts.Add(account);
            players.Add((platform, puuid, account.Id));

            var champions = ChampionPool.OrderBy(_ => random.Next()).Take(5).ToList();
            for (var c = 0; c < champions.Count; c++)
            {
                db.MainChampionStats.Add(MainChampionStatSeed.Row(
                    platform, puuid, champions[c], Lanes[random.Next(Lanes.Length)], isMain: c < 4));
            }
        }

        for (var i = 0; i < matches; i++)
        {
            var matchId = $"SCALING_{i}";
            var platform = Platforms[random.Next(Platforms.Length)];
            var duration = random.Next(4) == 0
                ? EdgeDurations[random.Next(EdgeDurations.Length)]
                : random.Next(200, 3000);

            var match = new MatchBuilder()
                .WithId(matchId)
                .WithPlatformId(platform)
                .WithQueueId(random.Next(20) == 0 ? OtherQueueId : QueueId)
                .WithGameVersion(Versions[random.Next(Versions.Length)])
                .WithGameDurationSeconds(duration)
                .WithTimelineIngested(random.Next(10) > 0)
                .Build();
            match.EndedInEarlySurrender = random.Next(25) == 0;
            db.Matches.Add(match);

            var blueWins = random.Next(2) == 0;
            var champions = ChampionPool.OrderBy(_ => random.Next()).Take(10).ToList();
            var onPlatform = players.Where(p => p.Platform == platform).OrderBy(_ => random.Next()).ToList();

            for (var seat = 0; seat < 10; seat++)
            {
                var team = seat < 5 ? 100 : 200;
                // A rare off-lane row: the cohort has no lane to file it under.
                var lane = random.Next(30) == 0 ? "" : Lanes[seat % 5];
                var tracked = random.Next(2) == 0 && seat < onPlatform.Count;

                db.MatchParticipants.Add(new MatchParticipant
                {
                    MatchId = matchId,
                    ParticipantId = seat + 1,
                    Puuid = tracked ? onPlatform[seat].Puuid : $"stranger-{matchId}-{seat}",
                    RiotAccountId = tracked ? onPlatform[seat].AccountId : null,
                    SummonerName = "seed",
                    SummonerLevel = 100,
                    ChampionId = champions[seat],
                    TeamId = team,
                    TeamPosition = lane,
                    IndividualPosition = lane,
                    Lane = lane,
                    Role = "SOLO",
                    Win = team == 100 ? blueWins : !blueWins,
                    EloBracket = StoredBands[random.Next(StoredBands.Length)],
                    ChampLevel = 16,
                    Item6 = 3363,
                    TrinketItemId = 3363,
                    ItemEvents = [],
                    SkillEvents = []
                });
            }
        }

        await db.SaveChangesAsync();
    }
}
