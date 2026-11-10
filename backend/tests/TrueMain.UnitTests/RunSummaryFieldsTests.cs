using System.Text.Json;
using AwesomeAssertions;
using Data.Pipeline;
using Ingestor.Processes.Summaries;

namespace TrueMain.UnitTests;

/// <summary>
/// The summary keys read back by the Riot budget ledgers and the Api's ops series are owned by
/// <see cref="RunSummaryFields"/>; each producer must write them under exactly that name.
/// </summary>
public sealed class RunSummaryFieldsTests
{
    [Fact]
    public void Discovery_WritesItsPerPlatformIntake()
    {
        var root = Serialize(new DiscoverySummary([new DiscoveryPlatformSummary("EUW1", 1, 1, 7, 0, 0, 0, 0, null, 0, 0)]));

        root.GetProperty(RunSummaryFields.Discovery.Platforms)[0]
            .GetProperty(RunSummaryFields.Discovery.CandidatesInserted).GetInt32().Should().Be(7);
    }

    [Fact]
    public void Harvest_WritesItsIntake_OnWorkAndNoWorkPasses()
    {
        Serialize(new HarvestSummary(5, 0, 0, 0, 0, 0, 0, false))
            .GetProperty(RunSummaryFields.Harvest.CandidatesInserted).GetInt32().Should().Be(5);
        Serialize(new HarvestNoWorkSummary("none", 0))
            .GetProperty(RunSummaryFields.Harvest.CandidatesInserted).GetInt32().Should().Be(0);
    }

    [Fact]
    public void ManualSeed_WritesItsQueuedCandidates()
        => Serialize(new ManualSeedSummary(1, 1, 0, 0, 3))
            .GetProperty(RunSummaryFields.ManualSeed.CandidatesQueued).GetInt32().Should().Be(3);

    [Fact]
    public void Scoring_WritesItsPerPlatformCounters()
    {
        var platform = Serialize(new ScoringSummary([new ScoringPlatformSummary("KR", 9, 4)], 10))
            .GetProperty(RunSummaryFields.Scoring.Platforms)[0];

        platform.GetProperty(RunSummaryFields.Scoring.Scored).GetInt32().Should().Be(9);
        platform.GetProperty(RunSummaryFields.Scoring.Queued).GetInt32().Should().Be(4);
    }

    [Fact]
    public void MatchIngestion_WritesTheSeriesCounters()
    {
        var root = Serialize(new MatchIngestionSummary(2, 11, 12, 13, 0, 14, 0, 0, 0, []));

        root.GetProperty(RunSummaryFields.MatchIngestion.MatchesInserted).GetInt32().Should().Be(11);
        root.GetProperty(RunSummaryFields.MatchIngestion.MatchesSkipped).GetInt32().Should().Be(12);
        root.GetProperty(RunSummaryFields.MatchIngestion.TimelinesUpdated).GetInt32().Should().Be(13);
        root.GetProperty(RunSummaryFields.MatchIngestion.AccountsValidated).GetInt32().Should().Be(14);
    }

    [Fact]
    public void MainAnalysis_WritesItsDemotions()
        => Serialize(new MainAnalysisSummary(1, 0, 0, 6))
            .GetProperty(RunSummaryFields.MainAnalysis.DemotedAccounts).GetInt32().Should().Be(6);

    [Fact]
    public void LadderSync_WritesWhatItsLedgerCharges()
    {
        var root = Serialize(new LadderSyncSummary(3, 8, 0, 0, 0, 0, 0, 0, []));

        root.GetProperty(RunSummaryFields.LadderSync.ApexCalls).GetInt32().Should().Be(3);
        root.GetProperty(RunSummaryFields.LadderSync.PagedCalls).GetInt32().Should().Be(8);
    }

    [Fact]
    public void PaceSampling_WritesWhatItsBudgetCharges()
        => Serialize(new PaceSamplingSummary(21, 0, 0, 0, 0, 0, 0, 0, []))
            .GetProperty(RunSummaryFields.PaceSampling.RiotCalls).GetInt32().Should().Be(21);

    private static JsonElement Serialize(IProcessRunSummary summary)
        => JsonDocument.Parse(ProcessRunSummaryJson.Serialize(summary)).RootElement.Clone();
}
