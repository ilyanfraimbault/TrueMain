using AwesomeAssertions;
using Data.Entities;
using TrueMain.Options;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops;

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

        signal.Status.Should().Be("unknown");
        signal.Key.Should().Be("processes");
    }

    [Fact]
    public void EvaluateProcesses_AllSuccess_ReturnsGreen()
    {
        var signal = PipelineHealthEvaluator.EvaluateProcesses(
        [
            Process("Discovery", nameof(ProcessRunStatus.Success)),
            Process("MatchIngestion", nameof(ProcessRunStatus.Success))
        ]);

        signal.Status.Should().Be("green");
        signal.Headline.Should().Contain("All 2 processes");
    }

    [Fact]
    public void EvaluateProcesses_NeverRun_ReturnsUnknown_NotAmber()
    {
        // "Never recorded a run" is an absence of measurement, not a warning — dressing it
        // as amber would put a caution colour on every process in a brand-new environment.
        var signal = PipelineHealthEvaluator.EvaluateProcesses(
        [
            Process("Discovery", PipelineHealthEvaluator.MissingStatus)
        ]);

        signal.Status.Should().Be("unknown");
        signal.UnknownReason.Should().Contain("Discovery");
    }

    [Fact]
    public void EvaluateProcesses_BrokenProcessOutranksAMissingOne()
    {
        // A process that actually failed is a stronger claim than one nobody has measured
        // yet, so a failed run must not be diluted into the same "unknown" bucket.
        var signal = PipelineHealthEvaluator.EvaluateProcesses(
        [
            Process("MatchIngestion", nameof(ProcessRunStatus.Failed)),
            Process("Discovery", PipelineHealthEvaluator.MissingStatus)
        ]);

        signal.Status.Should().Be("red");
        signal.Headline.Should().Contain("MatchIngestion");
    }

    [Fact]
    public void EvaluateProcesses_AbandonedRunIsAlsoRed_NotJustFailed()
    {
        // Abandoned (the run's host died mid-flight) is a different claim from Failed, but
        // both are a broken latest run and must roll up to the same red verdict.
        var signal = PipelineHealthEvaluator.EvaluateProcesses(
        [
            Process("MatchIngestion", nameof(ProcessRunStatus.Abandoned))
        ]);

        signal.Status.Should().Be("red");
    }

    // -- EvaluateDataQuality --------------------------------------------------

    [Fact]
    public void EvaluateDataQuality_NoDetectors_ReturnsUnknown()
    {
        var signal = PipelineHealthEvaluator.EvaluateDataQuality(new DataQualityDetectorsReadModel());

        signal.Status.Should().Be("unknown");
    }

    [Fact]
    public void EvaluateDataQuality_AllGreen_ReturnsGreenWithAllPassHeadline()
    {
        var signal = PipelineHealthEvaluator.EvaluateDataQuality(new DataQualityDetectorsReadModel
        {
            Detectors =
            [
                Detector("duplicateDimensionRows", "green"),
                Detector("ingestionLag", "green")
            ]
        });

        signal.Status.Should().Be("green");
        signal.Headline.Should().Be("All 2 checks pass.");
    }

    [Fact]
    public void EvaluateDataQuality_SomeFailing_CountsOnlyAmberAndRed()
    {
        var signal = PipelineHealthEvaluator.EvaluateDataQuality(new DataQualityDetectorsReadModel
        {
            Detectors =
            [
                Detector("duplicateDimensionRows", "green"),
                Detector("ingestionLag", "amber"),
                Detector("orphanParticipants", "red")
            ]
        });

        // Worst of {green, amber, red} is red, and exactly 2 detectors (amber + red) are
        // "failing" — green does not count towards the failing tally.
        signal.Status.Should().Be("red");
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
                Detector("duplicateDimensionRows", "green"),
                Detector("ingestionLag", "unknown", unknownReason: "Mongo is unreachable.")
            ]
        });

        signal.Status.Should().Be("unknown");
        signal.Headline.Should().Contain("Mongo is unreachable.");
    }

    // -- EvaluateDetectorAsSignal ---------------------------------------------

    [Fact]
    public void EvaluateDetectorAsSignal_MissingDetector_ReturnsUnknown_NamesTheKey()
    {
        var signal = PipelineHealthEvaluator.EvaluateDetectorAsSignal(
            new DataQualityDetectorsReadModel(), "ingestionLag", "ingestionLag", "Ingestion lag & queues");

        signal.Status.Should().Be("unknown");
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
                Detectors = [Detector("ingestionLag", "amber", headline: "Newest match is 6h old.")]
            },
            "ingestionLag", "ingestionLag", "Ingestion lag & queues");

        signal.Status.Should().Be("amber");
        signal.Headline.Should().Be("Newest match is 6h old.");
        signal.UnknownReason.Should().BeNull();
    }

    [Fact]
    public void EvaluateDetectorAsSignal_UnknownDetectorWithNoReason_FallsBackToAGenericOne()
    {
        var signal = PipelineHealthEvaluator.EvaluateDetectorAsSignal(
            new DataQualityDetectorsReadModel
            {
                Detectors = [Detector("ingestionLag", "unknown", unknownReason: null)]
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

        signal.Status.Should().Be("unknown");
    }

    [Fact]
    public void EvaluateDiskForecast_NoCapacityConfigured_ReturnsUnknown()
    {
        var history = new DbStorageHistoryReadModel { Daily = [new DbStorageDailyPoint()], ComparableDays = 5 };

        var signal = PipelineHealthEvaluator.EvaluateDiskForecast(
            history, configuredCapacityBytes: 0, Now, DefaultOptions());

        signal.Status.Should().Be("unknown");
        signal.UnknownReason.Should().Contain("StorageHistory:DiskCapacityBytes");
    }

    [Fact]
    public void EvaluateDiskForecast_FewerThanThreeComparableDays_ReturnsUnknown()
    {
        var history = new DbStorageHistoryReadModel { Daily = [new DbStorageDailyPoint()], ComparableDays = 2 };

        var signal = PipelineHealthEvaluator.EvaluateDiskForecast(
            history, configuredCapacityBytes: 1_000_000, Now, DefaultOptions());

        signal.Status.Should().Be("unknown");
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

        signal.Status.Should().Be("unknown");
    }

    [Fact]
    public void EvaluateDiskForecast_NoCrossingProjected_ReturnsGreen()
    {
        // A century-plus runway or no configured fill level: growing, but nothing to warn
        // about. This must read as a pass, not as "could not measure".
        var history = HistoryWithForecast([]);

        var signal = PipelineHealthEvaluator.EvaluateDiskForecast(
            history, configuredCapacityBytes: 1_000_000, Now, DefaultOptions());

        signal.Status.Should().Be("green");
    }

    [Fact]
    public void EvaluateDiskForecast_CrossingInsideRedWindow_ReturnsRed()
    {
        var history = HistoryWithForecast([(90, Now.AddDays(10))]);

        var signal = PipelineHealthEvaluator.EvaluateDiskForecast(
            history, configuredCapacityBytes: 1_000_000, Now,
            new PipelineHealthOptions { DiskForecastAmberDays = 90, DiskForecastRedDays = 30 });

        signal.Status.Should().Be("red");
    }

    [Fact]
    public void EvaluateDiskForecast_CrossingInsideAmberButNotRedWindow_ReturnsAmber()
    {
        var history = HistoryWithForecast([(90, Now.AddDays(60))]);

        var signal = PipelineHealthEvaluator.EvaluateDiskForecast(
            history, configuredCapacityBytes: 1_000_000, Now,
            new PipelineHealthOptions { DiskForecastAmberDays = 90, DiskForecastRedDays = 30 });

        signal.Status.Should().Be("amber");
    }

    [Fact]
    public void EvaluateDiskForecast_CrossingBeyondBothWindows_ReturnsGreen()
    {
        var history = HistoryWithForecast([(90, Now.AddDays(200))]);

        var signal = PipelineHealthEvaluator.EvaluateDiskForecast(
            history, configuredCapacityBytes: 1_000_000, Now,
            new PipelineHealthOptions { DiskForecastAmberDays = 90, DiskForecastRedDays = 30 });

        signal.Status.Should().Be("green");
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

        signal.Status.Should().Be("amber");
    }

    [Fact]
    public void EvaluateDiskForecast_AlreadyPastTheLevel_StillReadsRed_WithItsOwnHeadline()
    {
        var history = HistoryWithForecast([(90, Now.AddDays(-5))]);

        var signal = PipelineHealthEvaluator.EvaluateDiskForecast(
            history, configuredCapacityBytes: 1_000_000, Now,
            new PipelineHealthOptions { DiskForecastAmberDays = 90, DiskForecastRedDays = 30 });

        signal.Status.Should().Be("red");
        signal.Headline.Should().Contain("Already past");
    }

    // -- Rollup -----------------------------------------------------------------

    [Fact]
    public void Rollup_NoSignals_ReturnsUnknown()
    {
        var (status, headline) = PipelineHealthEvaluator.Rollup([]);

        status.Should().Be("unknown");
        headline.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Rollup_AllGreen_ReturnsGreenWithAllPassHeadline()
    {
        var (status, headline) = PipelineHealthEvaluator.Rollup(
        [
            Signal("processes", "green"),
            Signal("dataQuality", "green")
        ]);

        status.Should().Be("green");
        headline.Should().Be("All 2 signals pass.");
    }

    [Fact]
    public void Rollup_SingularFailingSignal_UsesSingularVerb()
    {
        var (status, headline) = PipelineHealthEvaluator.Rollup(
        [
            Signal("processes", "red"),
            Signal("dataQuality", "green"),
            Signal("diskForecast", "green")
        ]);

        status.Should().Be("red");
        headline.Should().Be("1 signal of 3 is failing.");
    }

    [Fact]
    public void Rollup_MultipleFailingSignals_UsesPluralVerb()
    {
        var (status, headline) = PipelineHealthEvaluator.Rollup(
        [
            Signal("processes", "red"),
            Signal("dataQuality", "amber")
        ]);

        status.Should().Be("red");
        headline.Should().Be("2 signals of 2 are failing.");
    }

    [Fact]
    public void Rollup_OnlyUnknownSignals_ReadsUnknown_NotGreen()
    {
        var (status, headline) = PipelineHealthEvaluator.Rollup(
        [
            Signal("processes", "unknown"),
            Signal("dataQuality", "green")
        ]);

        status.Should().Be("unknown");
        headline.Should().Be("1 signal of 2 could not be measured.");
    }

    // -- OrderBySeverity ----------------------------------------------------------

    [Fact]
    public void OrderBySeverity_SortsRedAmberUnknownGreen()
    {
        var ordered = PipelineHealthEvaluator.OrderBySeverity(
        [
            Signal("green-one", "green"),
            Signal("red-one", "red"),
            Signal("unknown-one", "unknown"),
            Signal("amber-one", "amber")
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
            Signal("first-red", "red"),
            Signal("second-red", "red")
        ]);

        ordered.Select(signal => signal.Key).Should().ContainInOrder("first-red", "second-red");
    }

    // -- fixtures -----------------------------------------------------------------

    private static ProcessHealthReadModel Process(string name, string status)
        => new() { ProcessName = name, Status = status };

    private static DataQualityDetectorReadModel Detector(
        string key, string status, string headline = "", string? unknownReason = null)
        => new()
        {
            Key = key,
            Status = status,
            Headline = headline,
            UnknownReason = unknownReason
        };

    private static PipelineHealthSignalReadModel Signal(string key, string status)
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
