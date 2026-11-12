using AwesomeAssertions;
using Data.Entities;
using TrueMain.Options;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Accounts;
using TrueMain.Services.Ops.Candidates;
using TrueMain.Services.Ops.Configuration;
using TrueMain.Services.Ops.Coverage;
using TrueMain.Services.Ops.DataQuality;
using TrueMain.Services.Ops.Database;
using TrueMain.Services.Ops.Diagnostics;
using TrueMain.Services.Ops.Health;
using TrueMain.Services.Ops.Processes;
using TrueMain.Services.Ops.Stats;

namespace TrueMain.UnitTests;

/// <summary>
/// The cockpit (#1031) exists so an operator does not have to open four pages to answer
/// "is the pipeline healthy?", so what these tests pin is the ways the rolled-up verdict
/// could mislead: a never-run process reading as a failure instead of unmeasured, a
/// composed tile disagreeing with the panel it links to, a disk forecast firing on a
/// disabled threshold, or the severity ordering hiding a red signal below a green one.
/// </summary>
public sealed class PipelineHealthEvaluatorTests
{
    private static readonly DateTime Now = new(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);

    // -- EvaluateProcesses ---------------------------------------------------

    [Fact]
    public void EvaluateProcesses_EmptyList_ReturnsUnknown_NotGreen()
    {
        // Nothing tracked is nothing judged — a fresh environment must not read "healthy"
        // just because it has no processes to fail.
        var signal = PipelineHealthEvaluator.EvaluateProcesses([]);

        signal.Status.Should().Be(DetectorStatus.Unknown);
        signal.Key.Should().Be("processes");
    }

    [Fact]
    public void EvaluateProcesses_AllSuccess_ReturnsGreen()
    {
        var signal = PipelineHealthEvaluator.EvaluateProcesses(
        [
            Process("Discovery", ProcessRunStatus.Success),
            Process("MatchIngestion", ProcessRunStatus.Success)
        ]);

        signal.Status.Should().Be(DetectorStatus.Green);
        signal.Headline.Should().Contain("All 2 processes");
    }

    [Fact]
    public void EvaluateProcesses_NeverRun_ReturnsUnknown_NotAmber()
    {
        // "Never recorded a run" is an absence of measurement, not a warning — dressing it
        // as amber would put a caution colour on every process in a brand-new environment.
        var signal = PipelineHealthEvaluator.EvaluateProcesses(
        [
            Process("Discovery", null)
        ]);

        signal.Status.Should().Be(DetectorStatus.Unknown);
        signal.UnknownReason.Should().Contain("Discovery");
    }

    [Fact]
    public void EvaluateProcesses_BrokenProcessOutranksAMissingOne()
    {
        // A process that actually failed is a stronger claim than one nobody has measured
        // yet, so a failed run must not be diluted into the same "unknown" bucket.
        var signal = PipelineHealthEvaluator.EvaluateProcesses(
        [
            Process("MatchIngestion", ProcessRunStatus.Failed),
            Process("Discovery", null)
        ]);

        signal.Status.Should().Be(DetectorStatus.Red);
        signal.Headline.Should().Contain("MatchIngestion");
    }

    [Fact]
    public void EvaluateProcesses_AbandonedRunIsAlsoRed_NotJustFailed()
    {
        // Abandoned (the run's host died mid-flight) is a different claim from Failed, but
        // both are a broken latest run and must roll up to the same red verdict.
        var signal = PipelineHealthEvaluator.EvaluateProcesses(
        [
            Process("MatchIngestion", ProcessRunStatus.Abandoned)
        ]);

        signal.Status.Should().Be(DetectorStatus.Red);
    }

    // -- EvaluateDataQuality --------------------------------------------------

    [Fact]
    public void EvaluateDataQuality_NoDetectors_ReturnsUnknown()
    {
        var signal = PipelineHealthEvaluator.EvaluateDataQuality(new DataQualityDetectorsReadModel());

        signal.Status.Should().Be(DetectorStatus.Unknown);
    }

    [Fact]
    public void EvaluateDataQuality_AllGreen_ReturnsGreenWithAllPassHeadline()
    {
        var signal = PipelineHealthEvaluator.EvaluateDataQuality(new DataQualityDetectorsReadModel
        {
            Detectors =
            [
                Detector("duplicateDimensionRows", DetectorStatus.Green),
                Detector("ingestionLag", DetectorStatus.Green)
            ]
        });

        signal.Status.Should().Be(DetectorStatus.Green);
        signal.Headline.Should().Be("All 2 checks pass.");
    }

    [Fact]
    public void EvaluateDataQuality_SomeFailing_CountsOnlyAmberAndRed()
    {
        var signal = PipelineHealthEvaluator.EvaluateDataQuality(new DataQualityDetectorsReadModel
        {
            Detectors =
            [
                Detector("duplicateDimensionRows", DetectorStatus.Green),
                Detector("ingestionLag", DetectorStatus.Amber),
                Detector("orphanParticipants", DetectorStatus.Red)
            ]
        });

        // Worst of {green, amber, red} is red, and exactly 2 detectors (amber + red) are
        // "failing" — green does not count towards the failing tally.
        signal.Status.Should().Be(DetectorStatus.Red);
        signal.Headline.Should().Be("2 checks are failing.");
    }

    [Fact]
    public void EvaluateDataQuality_WorstIsUnknown_ExplainsWhichCheckAndWhy()
    {
        // Unknown outranks green in the roll-up: one unmeasured detector must not let the
        // tile claim a pass while amber/red detectors, if any, are masked underneath.
        var signal = PipelineHealthEvaluator.EvaluateDataQuality(new DataQualityDetectorsReadModel
        {
            Detectors =
            [
                Detector("duplicateDimensionRows", DetectorStatus.Green),
                Detector("ingestionLag", DetectorStatus.Unknown, unknownReason: "Mongo is unreachable.")
            ]
        });

        signal.Status.Should().Be(DetectorStatus.Unknown);
        signal.Headline.Should().Contain("Mongo is unreachable.");
    }

    // -- EvaluateDetectorAsSignal ---------------------------------------------

    [Fact]
    public void EvaluateDetectorAsSignal_MissingDetector_ReturnsUnknown_NamesTheKey()
    {
        var signal = PipelineHealthEvaluator.EvaluateDetectorAsSignal(
            new DataQualityDetectorsReadModel(), "ingestionLag", "ingestionLag", "Ingestion lag & queues");

        signal.Status.Should().Be(DetectorStatus.Unknown);
        signal.UnknownReason.Should().Contain("ingestionLag");
    }

    [Fact]
    public void EvaluateDetectorAsSignal_LiftsStatusAndHeadlineVerbatim()
    {
        // The whole point of lifting rather than re-measuring: the cockpit tile and the
        // /data-quality card it links to must read the identical sentence.
        var signal = PipelineHealthEvaluator.EvaluateDetectorAsSignal(
            new DataQualityDetectorsReadModel
            {
                Detectors = [Detector("ingestionLag", DetectorStatus.Amber, headline: "Newest match is 6h old.")]
            },
            "ingestionLag", "ingestionLag", "Ingestion lag & queues");

        signal.Status.Should().Be(DetectorStatus.Amber);
        signal.Headline.Should().Be("Newest match is 6h old.");
        signal.UnknownReason.Should().BeNull();
    }

    [Fact]
    public void EvaluateDetectorAsSignal_UnknownDetectorWithNoReason_FallsBackToAGenericOne()
    {
        var signal = PipelineHealthEvaluator.EvaluateDetectorAsSignal(
            new DataQualityDetectorsReadModel
            {
                Detectors = [Detector("ingestionLag", DetectorStatus.Unknown, unknownReason: null)]
            },
            "ingestionLag", "ingestionLag", "Ingestion lag & queues");

        signal.UnknownReason.Should().NotBeNullOrEmpty();
    }

    // -- EvaluateDiskForecast ---------------------------------------------------

    [Fact]
    public void EvaluateDiskForecast_NoHistory_ReturnsUnknown()
    {
        var signal = PipelineHealthEvaluator.EvaluateDiskForecast(
            new DbStorageHistoryReadModel(), configuredCapacityBytes: 1_000_000, Now, DefaultOptions());

        signal.Status.Should().Be(DetectorStatus.Unknown);
    }

    [Fact]
    public void EvaluateDiskForecast_NoCapacityConfigured_ReturnsUnknown()
    {
        var history = new DbStorageHistoryReadModel { Daily = [new DbStorageDailyPoint()], ComparableDays = 5 };

        var signal = PipelineHealthEvaluator.EvaluateDiskForecast(
            history, configuredCapacityBytes: 0, Now, DefaultOptions());

        signal.Status.Should().Be(DetectorStatus.Unknown);
        signal.UnknownReason.Should().Contain("StorageHistory:DiskCapacityBytes");
    }

    [Fact]
    public void EvaluateDiskForecast_FewerThanThreeComparableDays_ReturnsUnknown()
    {
        var history = new DbStorageHistoryReadModel { Daily = [new DbStorageDailyPoint()], ComparableDays = 2 };

        var signal = PipelineHealthEvaluator.EvaluateDiskForecast(
            history, configuredCapacityBytes: 1_000_000, Now, DefaultOptions());

        signal.Status.Should().Be(DetectorStatus.Unknown);
    }

    [Fact]
    public void EvaluateDiskForecast_FlatOrShrinkingStorage_HasNullForecast_ReturnsUnknown()
    {
        var history = new DbStorageHistoryReadModel
        {
            Daily = [new DbStorageDailyPoint()],
            ComparableDays = 5,
            Forecast = null
        };

        var signal = PipelineHealthEvaluator.EvaluateDiskForecast(
            history, configuredCapacityBytes: 1_000_000, Now, DefaultOptions());

        signal.Status.Should().Be(DetectorStatus.Unknown);
    }

    [Fact]
    public void EvaluateDiskForecast_NoCrossingProjected_ReturnsGreen()
    {
        // A century-plus runway or no configured fill level: growing, but nothing to warn
        // about. This must read as a pass, not as "could not measure".
        var history = HistoryWithForecast([]);

        var signal = PipelineHealthEvaluator.EvaluateDiskForecast(
            history, configuredCapacityBytes: 1_000_000, Now, DefaultOptions());

        signal.Status.Should().Be(DetectorStatus.Green);
    }

    [Fact]
    public void EvaluateDiskForecast_CrossingInsideRedWindow_ReturnsRed()
    {
        var history = HistoryWithForecast([(90, Now.AddDays(10))]);

        var signal = PipelineHealthEvaluator.EvaluateDiskForecast(
            history, configuredCapacityBytes: 1_000_000, Now,
            new PipelineHealthOptions { DiskForecastAmberDays = 90, DiskForecastRedDays = 30 });

        signal.Status.Should().Be(DetectorStatus.Red);
    }

    [Fact]
    public void EvaluateDiskForecast_CrossingInsideAmberButNotRedWindow_ReturnsAmber()
    {
        var history = HistoryWithForecast([(90, Now.AddDays(60))]);

        var signal = PipelineHealthEvaluator.EvaluateDiskForecast(
            history, configuredCapacityBytes: 1_000_000, Now,
            new PipelineHealthOptions { DiskForecastAmberDays = 90, DiskForecastRedDays = 30 });

        signal.Status.Should().Be(DetectorStatus.Amber);
    }

    [Fact]
    public void EvaluateDiskForecast_CrossingBeyondBothWindows_ReturnsGreen()
    {
        var history = HistoryWithForecast([(90, Now.AddDays(200))]);

        var signal = PipelineHealthEvaluator.EvaluateDiskForecast(
            history, configuredCapacityBytes: 1_000_000, Now,
            new PipelineHealthOptions { DiskForecastAmberDays = 90, DiskForecastRedDays = 30 });

        signal.Status.Should().Be(DetectorStatus.Green);
    }

    [Fact]
    public void EvaluateDiskForecast_ANegativeThreshold_IsTreatedAsDisabled_NeverAsAlwaysFiring()
    {
        // Setting a level to 0 (or below) is documented as "disable it" — a crossing five
        // days out must not turn red just because the red window was switched off.
        var history = HistoryWithForecast([(90, Now.AddDays(5))]);

        var signal = PipelineHealthEvaluator.EvaluateDiskForecast(
            history, configuredCapacityBytes: 1_000_000, Now,
            new PipelineHealthOptions { DiskForecastAmberDays = 90, DiskForecastRedDays = 0 });

        signal.Status.Should().Be(DetectorStatus.Amber);
    }

    [Fact]
    public void EvaluateDiskForecast_AlreadyPastTheLevel_StillReadsRed_WithItsOwnHeadline()
    {
        var history = HistoryWithForecast([(90, Now.AddDays(-5))]);

        var signal = PipelineHealthEvaluator.EvaluateDiskForecast(
            history, configuredCapacityBytes: 1_000_000, Now,
            new PipelineHealthOptions { DiskForecastAmberDays = 90, DiskForecastRedDays = 30 });

        signal.Status.Should().Be(DetectorStatus.Red);
        signal.Headline.Should().Contain("Already past");
    }

    // -- Rollup -----------------------------------------------------------------

    [Fact]
    public void Rollup_NoSignals_ReturnsUnknown()
    {
        var (status, headline) = PipelineHealthEvaluator.Rollup([]);

        status.Should().Be(DetectorStatus.Unknown);
        headline.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Rollup_AllGreen_ReturnsGreenWithAllPassHeadline()
    {
        var (status, headline) = PipelineHealthEvaluator.Rollup(
        [
            Signal("processes", DetectorStatus.Green),
            Signal("dataQuality", DetectorStatus.Green)
        ]);

        status.Should().Be(DetectorStatus.Green);
        headline.Should().Be("All 2 signals pass.");
    }

    [Fact]
    public void Rollup_SingularFailingSignal_UsesSingularVerb()
    {
        var (status, headline) = PipelineHealthEvaluator.Rollup(
        [
            Signal("processes", DetectorStatus.Red),
            Signal("dataQuality", DetectorStatus.Green),
            Signal("diskForecast", DetectorStatus.Green)
        ]);

        status.Should().Be(DetectorStatus.Red);
        headline.Should().Be("1 signal of 3 is failing.");
    }

    [Fact]
    public void Rollup_MultipleFailingSignals_UsesPluralVerb()
    {
        var (status, headline) = PipelineHealthEvaluator.Rollup(
        [
            Signal("processes", DetectorStatus.Red),
            Signal("dataQuality", DetectorStatus.Amber)
        ]);

        status.Should().Be(DetectorStatus.Red);
        headline.Should().Be("2 signals of 2 are failing.");
    }

    [Fact]
    public void Rollup_OnlyUnknownSignals_ReadsUnknown_NotGreen()
    {
        var (status, headline) = PipelineHealthEvaluator.Rollup(
        [
            Signal("processes", DetectorStatus.Unknown),
            Signal("dataQuality", DetectorStatus.Green)
        ]);

        status.Should().Be(DetectorStatus.Unknown);
        headline.Should().Be("1 signal of 2 could not be measured.");
    }

    // -- OrderBySeverity ----------------------------------------------------------

    [Fact]
    public void OrderBySeverity_SortsRedAmberUnknownGreen()
    {
        var ordered = PipelineHealthEvaluator.OrderBySeverity(
        [
            Signal("green-one", DetectorStatus.Green),
            Signal("red-one", DetectorStatus.Red),
            Signal("unknown-one", DetectorStatus.Unknown),
            Signal("amber-one", DetectorStatus.Amber)
        ]);

        ordered.Select(signal => signal.Key).Should().ContainInOrder("red-one", "amber-one", "unknown-one", "green-one");
    }

    [Fact]
    public void OrderBySeverity_TiesKeepTheirDeclarationOrder()
    {
        // Ties are the pipeline's own order (declaration order in GetAsync), not an
        // arbitrary re-sort — a stable sort is load-bearing here.
        var ordered = PipelineHealthEvaluator.OrderBySeverity(
        [
            Signal("first-red", DetectorStatus.Red),
            Signal("second-red", DetectorStatus.Red)
        ]);

        ordered.Select(signal => signal.Key).Should().ContainInOrder("first-red", "second-red");
    }

    // -- fixtures -----------------------------------------------------------------

    private static ProcessHealthReadModel Process(string name, ProcessRunStatus? status)
        => new() { ProcessName = name, Status = status };

    private static DataQualityDetectorReadModel Detector(
        string key, DetectorStatus status, string headline = "", string? unknownReason = null)
        => new()
        {
            Key = key,
            Status = status,
            Headline = headline,
            UnknownReason = unknownReason
        };

    private static PipelineHealthSignalReadModel Signal(string key, DetectorStatus status)
        => new() { Key = key, Status = status };

    private static PipelineHealthOptions DefaultOptions() => new();

    private static DbStorageHistoryReadModel HistoryWithForecast(
        IReadOnlyList<(double Percent, DateTime? ProjectedAtUtc)> crossings)
        => new()
        {
            Daily = [new DbStorageDailyPoint()],
            ComparableDays = 5,
            Forecast = new DbStorageForecast
            {
                BytesPerDay = 1_000,
                DiskCapacityBytes = 1_000_000,
                Crossings = [.. crossings.Select(crossing => new DbStorageThresholdCrossing
                {
                    Percent = crossing.Percent,
                    ThresholdBytes = 900_000,
                    ProjectedAtUtc = crossing.ProjectedAtUtc
                })]
            }
        };
}
