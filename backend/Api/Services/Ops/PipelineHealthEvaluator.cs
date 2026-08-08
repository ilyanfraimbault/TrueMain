using System.Globalization;
using Data.Entities;
using TrueMain.Options;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// The judgement half of the operator cockpit (#1031): everything that turns the measured
/// signals into per-tile verdicts and one rolled-up answer, with no database and no clock
/// of its own.
///
/// <para>
/// Pure and separate for the same reason <see cref="DataQualityDetectorEvaluator"/> and
/// <see cref="StorageForecastCalculator"/> are: the thresholds and the "what counts as
/// healthy" calls are the part that will be argued about, so they have to be testable
/// without a Postgres container. It borrows that class's <see cref="DetectorStatus"/>
/// vocabulary and its <c>Worst</c> precedence on purpose — the cockpit's dots and the
/// data-quality panel's dots have to mean the same thing.
/// </para>
/// </summary>
internal static class PipelineHealthEvaluator
{
    /// <summary>Admin routes owning each signal's depth. The cockpit holds no detail itself.</summary>
    private const string ProcessesPath = "/processes";
    private const string DataQualityPath = "/data-quality";
    private const string DatabasePath = "/database";

    /// <summary>
    /// Judges the per-process rollup.
    ///
    /// <para>
    /// A failed or abandoned latest run is red; a process that has never recorded a run is
    /// <em>unknown</em>, not amber — "never ran" is an absence of measurement, and on a
    /// fresh environment all ten are absent. Deliberately does <b>not</b> judge how long
    /// ago a process last succeeded: the ten processes have wildly different cadences and
    /// inventing a per-process expectation here would be a second, competing source of
    /// truth. "The pipeline has stopped" is what the ingestion-lag detector and raw-data
    /// freshness are for.
    /// </para>
    /// </summary>
    public static PipelineHealthSignalReadModel EvaluateProcesses(IReadOnlyList<ProcessHealthReadModel> processes)
    {
        ArgumentNullException.ThrowIfNull(processes);

        if (processes.Count == 0)
        {
            return Unknown(
                "processes",
                "Processes",
                ProcessesPath,
                "No process is being tracked, so nothing could be judged.");
        }

        var broken = processes
            .Where(process => process.Status is nameof(ProcessRunStatus.Failed) or nameof(ProcessRunStatus.Abandoned))
            .ToList();
        var missing = processes.Where(process => process.Status == MissingStatus).ToList();

        if (broken.Count > 0)
        {
            return new PipelineHealthSignalReadModel
            {
                Key = "processes",
                Title = "Processes",
                Status = DetectorStatus.Red.ToWireName(),
                Headline = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Plural(broken.Count, "process", "processes")} of {processes.Count} last ended badly: {NameList(broken)}."),
                DetailPath = ProcessesPath
            };
        }

        if (missing.Count > 0)
        {
            return Unknown(
                "processes",
                "Processes",
                ProcessesPath,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Plural(missing.Count, "process", "processes")} of {processes.Count} have never recorded a run: {NameList(missing)}."));
        }

        return new PipelineHealthSignalReadModel
        {
            Key = "processes",
            Title = "Processes",
            Status = DetectorStatus.Green.ToWireName(),
            Headline = string.Create(
                CultureInfo.InvariantCulture,
                $"All {processes.Count} processes last ran without failing."),
            DetailPath = ProcessesPath
        };
    }

    /// <summary>
    /// Rolls the five data-quality detectors into the cockpit's one-line verdict, reusing
    /// their own statuses rather than re-measuring anything. The wording matches the
    /// <c>/data-quality</c> page's opening line so the tile and its destination read the
    /// same.
    /// </summary>
    public static PipelineHealthSignalReadModel EvaluateDataQuality(DataQualityDetectorsReadModel detectors)
    {
        ArgumentNullException.ThrowIfNull(detectors);

        if (detectors.Detectors.Count == 0)
        {
            return Unknown(
                "dataQuality",
                "Data quality",
                DataQualityPath,
                "The detectors returned nothing, so no verdict could be formed.");
        }

        var statuses = detectors.Detectors.Select(detector => FromWireName(detector.Status)).ToList();
        var worst = DataQualityDetectorEvaluator.Worst(statuses);
        var failing = statuses.Count(status => status is DetectorStatus.Amber or DetectorStatus.Red);

        if (worst == DetectorStatus.Unknown)
        {
            var unmeasured = detectors.Detectors
                .Where(detector => FromWireName(detector.Status) == DetectorStatus.Unknown)
                .ToList();

            return Unknown(
                "dataQuality",
                "Data quality",
                DataQualityPath,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Plural(unmeasured.Count, "check", "checks")} of {detectors.Detectors.Count} could not be measured: {FirstReason(unmeasured)}"));
        }

        return new PipelineHealthSignalReadModel
        {
            Key = "dataQuality",
            Title = "Data quality",
            Status = worst.ToWireName(),
            Headline = failing == 0
                ? string.Create(CultureInfo.InvariantCulture, $"All {detectors.Detectors.Count} checks pass.")
                : string.Create(CultureInfo.InvariantCulture, $"{Plural(failing, "check", "checks")} are failing."),
            DetailPath = DataQualityPath
        };
    }

    /// <summary>
    /// Lifts one named detector out of the data-quality payload as its own tile, verbatim —
    /// same status, same sentence. Used for the ingestion-lag/queue-depth signal, which the
    /// cockpit wants at the top level rather than buried in a five-item list.
    /// </summary>
    public static PipelineHealthSignalReadModel EvaluateDetectorAsSignal(
        DataQualityDetectorsReadModel detectors,
        string detectorKey,
        string signalKey,
        string title)
    {
        ArgumentNullException.ThrowIfNull(detectors);

        var detector = detectors.Detectors.FirstOrDefault(candidate => candidate.Key == detectorKey);
        if (detector is null)
        {
            return Unknown(
                signalKey,
                title,
                DataQualityPath,
                string.Create(CultureInfo.InvariantCulture, $"The '{detectorKey}' detector did not report."));
        }

        var status = FromWireName(detector.Status);

        return new PipelineHealthSignalReadModel
        {
            Key = signalKey,
            Title = title,
            Status = status.ToWireName(),
            Headline = detector.Headline,
            UnknownReason = status == DetectorStatus.Unknown
                ? detector.UnknownReason ?? "The detector could not measure this."
                : null,
            DetailPath = DataQualityPath
        };
    }

    /// <summary>
    /// Judges the disk forecast, keeping its three <em>absent</em> states apart instead of
    /// collapsing them into one "no data" shrug — the whole point of #925's forecast is that
    /// it is absent rather than guessed, and prod currently sits in the
    /// no-configured-capacity state.
    /// </summary>
    public static PipelineHealthSignalReadModel EvaluateDiskForecast(
        DbStorageHistoryReadModel history,
        long configuredCapacityBytes,
        DateTime nowUtc,
        PipelineHealthOptions options)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(options);

        if (history.Daily.Count == 0)
        {
            return Unknown(
                "diskForecast",
                "Disk forecast",
                DatabasePath,
                "No storage snapshot has been recorded yet.");
        }

        if (configuredCapacityBytes <= 0)
        {
            return Unknown(
                "diskForecast",
                "Disk forecast",
                DatabasePath,
                "No disk capacity is configured (StorageHistory:DiskCapacityBytes), so no fill date can be projected.");
        }

        if (history.ComparableDays < 3)
        {
            return Unknown(
                "diskForecast",
                "Disk forecast",
                DatabasePath,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Only {Plural(history.ComparableDays, "day", "days")} of history measure the current engines — under the 3 needed to fit a growth rate."));
        }

        if (history.Forecast is null)
        {
            return Unknown(
                "diskForecast",
                "Disk forecast",
                DatabasePath,
                "Storage is flat or shrinking over the window, so no growth rate could be fitted.");
        }

        var nextCrossing = history.Forecast.Crossings
            .Where(crossing => crossing.ProjectedAtUtc is not null)
            .OrderBy(crossing => crossing.ProjectedAtUtc!.Value)
            .FirstOrDefault();

        if (nextCrossing is null)
        {
            return new PipelineHealthSignalReadModel
            {
                Key = "diskForecast",
                Title = "Disk forecast",
                Status = DetectorStatus.Green.ToWireName(),
                Headline = "Growing, with no configured fill level projected within a century.",
                DetailPath = DatabasePath
            };
        }

        var daysAway = (nextCrossing.ProjectedAtUtc!.Value - nowUtc).TotalDays;

        // Fewer days is worse, the inverse of every other threshold here, so the comparison
        // is spelled out rather than run through Classify (which models "more is worse").
        var status = options.DiskForecastRedDays > 0 && daysAway <= options.DiskForecastRedDays
            ? DetectorStatus.Red
            : options.DiskForecastAmberDays > 0 && daysAway <= options.DiskForecastAmberDays
                ? DetectorStatus.Amber
                : DetectorStatus.Green;

        var headline = daysAway < 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Already past {nextCrossing.Percent:F0}% of capacity.")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{nextCrossing.Percent:F0}% of capacity in {daysAway:F0} days.");

        return new PipelineHealthSignalReadModel
        {
            Key = "diskForecast",
            Title = "Disk forecast",
            Status = status.ToWireName(),
            Headline = headline,
            DetailPath = DatabasePath
        };
    }

    /// <summary>
    /// The single verdict: the worst signal, with the same <c>red &gt; amber &gt; unknown
    /// &gt; green</c> precedence the detectors use, plus the sentence that goes above the
    /// tiles.
    /// </summary>
    public static (string Status, string Headline) Rollup(IReadOnlyList<PipelineHealthSignalReadModel> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);

        if (signals.Count == 0)
        {
            return (DetectorStatus.Unknown.ToWireName(), "No signal could be measured.");
        }

        var statuses = signals.Select(signal => FromWireName(signal.Status)).ToList();
        var worst = DataQualityDetectorEvaluator.Worst(statuses);
        var failing = statuses.Count(status => status is DetectorStatus.Amber or DetectorStatus.Red);
        var unmeasured = statuses.Count(status => status == DetectorStatus.Unknown);

        var headline = worst switch
        {
            DetectorStatus.Green => string.Create(
                CultureInfo.InvariantCulture,
                $"All {signals.Count} signals pass."),
            DetectorStatus.Unknown => string.Create(
                CultureInfo.InvariantCulture,
                $"{Plural(unmeasured, "signal", "signals")} of {signals.Count} could not be measured."),
            _ => string.Create(
                CultureInfo.InvariantCulture,
                $"{Plural(failing, "signal", "signals")} of {signals.Count} {(failing == 1 ? "is" : "are")} failing."),
        };

        return (worst.ToWireName(), headline);
    }

    /// <summary>
    /// Severity-ordered for display — worst first, so the thing to act on is the thing at
    /// the top. Ties keep their declaration order, which is the pipeline's own order.
    /// </summary>
    public static IReadOnlyList<PipelineHealthSignalReadModel> OrderBySeverity(
        IEnumerable<PipelineHealthSignalReadModel> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);

        return signals
            .OrderByDescending(signal => FromWireName(signal.Status) switch
            {
                DetectorStatus.Red => 3,
                DetectorStatus.Amber => 2,
                DetectorStatus.Unknown => 1,
                _ => 0
            })
            .ToList();
    }

    /// <summary>The synthetic status for a process with no recorded run.</summary>
    public const string MissingStatus = "Missing";

    private static PipelineHealthSignalReadModel Unknown(string key, string title, string detailPath, string reason)
        => new()
        {
            Key = key,
            Title = title,
            Status = DetectorStatus.Unknown.ToWireName(),
            // The tile shows the reason in place of a number. A zero here would read as a
            // pass, which is the failure mode #1031 exists to prevent.
            Headline = reason,
            UnknownReason = reason,
            DetailPath = detailPath
        };

    private static DetectorStatus FromWireName(string status)
        => status switch
        {
            "green" => DetectorStatus.Green,
            "amber" => DetectorStatus.Amber,
            "red" => DetectorStatus.Red,
            _ => DetectorStatus.Unknown
        };

    private static string Plural(int count, string singular, string plural)
        => string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? singular : plural)}");

    private static string NameList(IEnumerable<ProcessHealthReadModel> processes)
        => string.Join(", ", processes.Select(process => process.ProcessName));

    private static string FirstReason(IEnumerable<DataQualityDetectorReadModel> detectors)
        => detectors
               .Select(detector => detector.UnknownReason)
               .FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason))
           ?? "no reason reported.";
}
