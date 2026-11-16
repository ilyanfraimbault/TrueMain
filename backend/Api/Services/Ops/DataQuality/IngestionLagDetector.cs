using System.Globalization;
using Core.Options;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.DataQuality;

/// <summary>
/// The ingestion-lag detector: the newest ranked match per platform, plus the two queue
/// depths (pending timelines, main candidates) that tell a backlog from a stall.
/// </summary>
internal sealed class IngestionLagDetector(TrueMainDbContext db, IOptions<MainAnalysisOptions> mainAnalysisOptions)
{
    public async Task<DataQualityDetectorReadModel> BuildAsync(
        DataQualityDetectorOptions settings,
        DateTime now,
        CancellationToken ct)
    {
        var queueId = (int)mainAnalysisOptions.Value.QueueId;

        var newestByPlatform = await db.Matches
            .AsNoTracking()
            .Where(match => match.QueueId == queueId)
            .GroupBy(match => match.PlatformId)
            .Select(group => new
            {
                PlatformId = group.Key,
                NewestStartUtc = group.Max(match => match.GameStartTimeUtc)
            })
            .ToListAsync(ct);

        var rows = new List<DataQualityDetectorRowReadModel>();
        long lagging = 0;

        foreach (var platform in newestByPlatform.OrderBy(platform => platform.PlatformId, StringComparer.Ordinal))
        {
            var age = DataQualityDetectorEvaluator.AgeHours(platform.NewestStartUtc, now);
            var status = DataQualityDetectorEvaluator.Classify(
                age,
                settings.IngestionLagAmberHours,
                settings.IngestionLagRedHours);

            if (status is DetectorStatus.Amber or DetectorStatus.Red)
            {
                lagging++;
            }

            rows.Add(new DataQualityDetectorRowReadModel
            {
                Label = platform.PlatformId,
                Status = status,
                Value = age,
                ValueLabel = DataQualityDetectorEvaluator.FormatAge(age),
                Note = "Newest ranked match ingested on this platform."
            });
        }

        // A tracked platform with no match at all never appears in the GROUP BY, so it
        // would silently drop off the card — the exact shape of "ingestion never started
        // here" that this detector is for.
        rows.AddRange(DetectorCards.MissingPlatformRows(
            await DetectorCards.LoadTrackedPlatformsAsync(db, ct),
            newestByPlatform.Select(platform => platform.PlatformId),
            "Tracked accounts on this platform, but no ranked match ingested."));

        if (rows.Count == 0)
        {
            // "Every platform is fresh" is vacuously true with no platforms, and reads as
            // a pass on a database that has ingested nothing at all.
            rows.Add(new DataQualityDetectorRowReadModel
            {
                Label = "platforms",
                Status = DetectorStatus.Unknown,
                Note = "No tracked platform and no ranked match, so ingestion lag cannot be measured."
            });
        }

        var pendingTimelines = await db.Matches
            .AsNoTracking()
            .LongCountAsync(match => !match.TimelineIngested, ct);

        var candidateCounts = await db.MainCandidates
            .AsNoTracking()
            .Where(candidate => candidate.Status == MainCandidateStatus.Queued
                || candidate.Status == MainCandidateStatus.Processing)
            .GroupBy(candidate => candidate.Status)
            .Select(group => new { Status = group.Key, Count = group.LongCount() })
            .ToListAsync(ct);

        var queued = candidateCounts.FirstOrDefault(row => row.Status == MainCandidateStatus.Queued)?.Count ?? 0;
        var processing = candidateCounts.FirstOrDefault(row => row.Status == MainCandidateStatus.Processing)?.Count ?? 0;

        QueueDepth[] queues =
        [
            new(
                "Matches awaiting a timeline",
                pendingTimelines,
                settings.PendingTimelineAmber,
                settings.PendingTimelineRed,
                "Normal backlog while MatchIngestion catches up; a standing pile means timelines are failing."),
            new(
                "Candidates queued",
                queued,
                settings.QueuedCandidatesAmber,
                settings.QueuedCandidatesRed,
                "Work waiting for MainAnalysis."),
            new(
                "Candidates processing",
                processing,
                settings.ProcessingCandidatesAmber,
                settings.ProcessingCandidatesRed,
                "Processing is a lease state — a large standing population means leases are leaking, not that work is queued.")
        ];
        rows.AddRange(queues.Select(QueueDepthRow));

        var lagStatus = DataQualityDetectorEvaluator.Worst(rows.Select(row => row.Status));

        return new DataQualityDetectorReadModel
        {
            Key = "ingestionLag",
            Title = "Ingestion lag & queues",
            Status = lagStatus,
            Count = lagging,
            CountLabel = "platforms behind their ingestion line",
            Headline = Headline(lagStatus, lagging, queues),
            SourceNote = "Newest match per platform (grouped over the ranked queue), plus the two queue depths that "
                + "distinguish a backlog from a stall. Same order of cost as the overview panel's counts.",
            Rows = rows,
            Thresholds =
            [
                DetectorCards.Threshold("newest match age", settings.IngestionLagAmberHours, settings.IngestionLagRedHours, DetectorThresholdUnit.Hours),
                DetectorCards.Threshold("matches awaiting a timeline", settings.PendingTimelineAmber, settings.PendingTimelineRed, DetectorThresholdUnit.Count),
                DetectorCards.Threshold("candidates queued", settings.QueuedCandidatesAmber, settings.QueuedCandidatesRed, DetectorThresholdUnit.Count),
                DetectorCards.Threshold("candidates processing", settings.ProcessingCandidatesAmber, settings.ProcessingCandidatesRed, DetectorThresholdUnit.Count)
            ]
        };
    }

    /// <summary>
    /// The card's sentence, built from the rows that fired: a queue that crossed its line
    /// is named with its depth and the line, so a verdict tripped by a backlog never reads
    /// as "0 platform(s) behind" (#2459).
    /// </summary>
    internal static string Headline(DetectorStatus status, long lagging, IReadOnlyList<QueueDepth> queues)
    {
        if (status == DetectorStatus.Green)
        {
            return "Every platform has ingested a match within its expected window.";
        }

        if (status == DetectorStatus.Unknown)
        {
            return "Ingestion lag could not be measured on part of the corpus.";
        }

        var causes = new List<string>();
        if (lagging > 0)
        {
            causes.Add(lagging == 1
                ? "1 platform has not ingested a recent match"
                : string.Create(CultureInfo.InvariantCulture, $"{lagging} platforms have not ingested a recent match"));
        }

        foreach (var queue in queues)
        {
            var queueStatus = queue.Status;
            if (queueStatus is DetectorStatus.Amber or DetectorStatus.Red)
            {
                var line = queueStatus == DetectorStatus.Red ? queue.Red : queue.Amber;
                causes.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{queue.Value:N0} {char.ToLowerInvariant(queue.Label[0])}{queue.Label[1..]} ({queueStatus.WireName()} from {line:N0})"));
            }
        }

        // Unreachable while the verdict is the worst of these rows, but a sentence that
        // names nothing must still not read as a pass.
        return causes.Count == 0
            ? "A platform or a queue is past its line."
            : string.Concat(char.ToUpperInvariant(causes[0][0]), string.Join("; ", causes)[1..], ".");
    }

    private static DataQualityDetectorRowReadModel QueueDepthRow(QueueDepth queue) => new()
    {
        Label = queue.Label,
        Status = queue.Status,
        Value = queue.Value,
        ValueLabel = string.Create(CultureInfo.InvariantCulture, $"{queue.Value}"),
        Note = queue.Note
    };

    /// <summary>One queue the card watches, with the two lines it is judged against.</summary>
    internal sealed record QueueDepth(string Label, long Value, long Amber, long Red, string Note = "")
    {
        public DetectorStatus Status => DataQualityDetectorEvaluator.Classify(Value, Amber, Red);
    }
}
