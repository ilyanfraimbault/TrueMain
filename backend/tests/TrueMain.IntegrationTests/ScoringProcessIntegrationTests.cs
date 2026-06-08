using Data.Entities;
using AwesomeAssertions;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.Coverage;
using Microsoft.Extensions.Logging.Abstractions;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ScoringProcessIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public ScoringProcessIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunAsync_ShouldScoreCandidatesAndQueueTopEntriesPerPlatform()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedCandidatesAsync();

        var process = new ScoringProcess(
            NullLogger<ScoringProcess>.Instance,
            _fixture.CreateSessionFactory(),
            new ChampionCoverageProvider(Microsoft.Extensions.Options.Options.Create(new CoverageOptions())),
            Microsoft.Extensions.Options.Options.Create(new ScoringOptions
            {
                BatchSize = 10,
                TopNPerPlatform = 1,
                TopChampionsPerAccount = 10,
                MaxLastPlayDays = 10,
                RecencyWeight = 0.65,
                RankWeight = 0.20,
                PointsWeight = 0.15
            }));

        await process.RunCoreAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        var candidates = verifyDb.MainCandidates
            .Where(c => c.PlatformId == "KR" && c.Puuid == "puuid-score-1")
            .OrderBy(c => c.ChampionId)
            .ToList();

        candidates.Should().HaveCount(2);
        candidates.Should().OnlyContain(candidate => candidate.Score > 0);
        candidates.Should().OnlyContain(candidate => candidate.ScoredAtUtc != null);
        candidates.Count(candidate => candidate.Status == MainCandidateStatus.Queued).Should().Be(1);
        candidates.Count(candidate => candidate.Status == MainCandidateStatus.Scored).Should().Be(1);
        candidates.Single(candidate => candidate.Status == MainCandidateStatus.Queued).ChampionId.Should().Be(22);
    }

    [Fact]
    public async Task RunAsync_ShouldQueueHighestScoreEvenAcrossMultipleScoringBatches()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedCandidatesAsync();

        var process = new ScoringProcess(
            NullLogger<ScoringProcess>.Instance,
            _fixture.CreateSessionFactory(),
            new ChampionCoverageProvider(Microsoft.Extensions.Options.Options.Create(new CoverageOptions())),
            Microsoft.Extensions.Options.Options.Create(new ScoringOptions
            {
                BatchSize = 1,
                TopNPerPlatform = 1,
                TopChampionsPerAccount = 10,
                MaxLastPlayDays = 10,
                RecencyWeight = 0.65,
                RankWeight = 0.20,
                PointsWeight = 0.15
            }));

        await process.RunCoreAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        var queuedCandidate = verifyDb.MainCandidates
            .Single(c => c.PlatformId == "KR" && c.Puuid == "puuid-score-1" && c.Status == MainCandidateStatus.Queued);

        queuedCandidate.ChampionId.Should().Be(22);
        queuedCandidate.Score.Should().BeGreaterThan(
            verifyDb.MainCandidates
                .Single(c => c.PlatformId == "KR" && c.Puuid == "puuid-score-1" && c.Status == MainCandidateStatus.Scored)
                .Score);
    }

    private async Task SeedCandidatesAsync()
    {
        await using var db = _fixture.CreateDbContext();
        var now = DateTime.UtcNow;

        db.MainCandidates.AddRange(
            new MainCandidate
            {
                PlatformId = "KR",
                Puuid = "puuid-score-1",
                ChampionId = 22,
                ChampionRankInMasteryTop = 1,
                ChampionPoints = 750_000,
                LastPlayTimeUtc = now.AddDays(-1),
                DiscoveredAtUtc = now.AddHours(-1),
                Status = MainCandidateStatus.New
            },
            new MainCandidate
            {
                PlatformId = "KR",
                Puuid = "puuid-score-1",
                ChampionId = 51,
                ChampionRankInMasteryTop = 5,
                ChampionPoints = 80_000,
                LastPlayTimeUtc = now.AddDays(-8),
                DiscoveredAtUtc = now.AddHours(-1),
                Status = MainCandidateStatus.New
            });

        await db.SaveChangesAsync();
    }

}
