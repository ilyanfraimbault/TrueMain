using System.Globalization;
using Core.Options;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
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
                Status = status.ToWireName(),
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
                Status = DetectorStatus.Unknown.ToWireName(),
                Note = "No tracked platform and no ranked match, so ingestion lag cannot be measured."
            });
        }

        var pendingTimelines = await db.Matches
            .AsNoTracking()
            .LongCountAsync(match => !match.TimelineIngested, ct);
        rows.Add(QueueDepthRow(
            "Matches awaiting a timeline",
            pendingTimelines,
            settings.PendingTimelineAmber,
            settings.PendingTimelineRed,
            "Normal backlog while MatchIngestion catches up; a standing pile means timelines are failing."));

        var candidateCounts = await db.MainCandidates
            .AsNoTracking()
            .Where(candidate => candidate.Status == MainCandidateStatus.Queued
                || candidate.Status == MainCandidateStatus.Processing)
            .GroupBy(candidate => candidate.Status)
            .Select(group => new { Status = group.Key, Count = group.LongCount() })
            .ToListAsync(ct);

        var queued = candidateCounts.FirstOrDefault(row => row.Status == MainCandidateStatus.Queued)?.Count ?? 0;
        var processing = candidateCounts.FirstOrDefault(row => row.Status == MainCandidateStatus.Processing)?.Count ?? 0;

        rows.Add(QueueDepthRow(
            "Candidates queued",
            queued,
            settings.QueuedCandidatesAmber,
            settings.QueuedCandidatesRed,
            "Work waiting for MainAnalysis."));
        rows.Add(QueueDepthRow(
            "Candidates processing",
            processing,
            settings.ProcessingCandidatesAmber,
            settings.ProcessingCandidatesRed,
            "Processing is a lease state — a large standing population means leases are leaking, not that work is queued."));

        var lagStatus = DataQualityDetectorEvaluator.Worst(rows.Select(DetectorCards.ParseStatus));

        return new DataQualityDetectorReadModel
        {
            Key = "ingestionLag",
            Title = "Ingestion lag & queues",
            Status = lagStatus.ToWireName(),
            Count = lagging,
            CountLabel = "platforms behind their ingestion line",
            Headline = lagStatus switch
            {
                DetectorStatus.Green => "Every platform has ingested a match within its expected window.",
                DetectorStatus.Unknown => "Ingestion lag could not be measured on part of the corpus.",
                _ => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{lagging} platform(s) have not ingested a recent match, or a queue has stopped draining."),
            },
            SourceNote = "Newest match per platform (grouped over the ranked queue), plus the two queue depths that "
                + "distinguish a backlog from a stall. Same order of cost as the overview panel's counts.",
            Rows = rows,
            Thresholds =
            [
                DetectorCards.Threshold("newest match age", settings.IngestionLagAmberHours, settings.IngestionLagRedHours, "hours"),
                DetectorCards.Threshold("matches awaiting a timeline", settings.PendingTimelineAmber, settings.PendingTimelineRed, "count"),
                DetectorCards.Threshold("candidates queued", settings.QueuedCandidatesAmber, settings.QueuedCandidatesRed, "count"),
                DetectorCards.Threshold("candidates processing", settings.ProcessingCandidatesAmber, settings.ProcessingCandidatesRed, "count")
            ]
        };
    }

    private static DataQualityDetectorRowReadModel QueueDepthRow(
        string label,
        long value,
        long amber,
        long red,
        string note) => new()
        {
            Label = label,
            Status = DataQualityDetectorEvaluator.Classify(value, amber, red).ToWireName(),
            Value = value,
            ValueLabel = string.Create(CultureInfo.InvariantCulture, $"{value}"),
            Note = note
        };
}
