using System.Globalization;
using Core.Options;
using Data;
using Data.Ops.Mongo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.DataQuality;

/// <summary>
/// The orphan-participants detector: the untracked share of the newest ranked matches per
/// platform, split into two windows for the trend, plus the freshness of Harvest — the
/// process that turns orphan participants into candidates.
/// </summary>
internal sealed class OrphanParticipantsDetector(
    TrueMainDbContext db,
    IProcessRunStore processRunStore,
    IOptions<MainAnalysisOptions> mainAnalysisOptions)
{
    private const string HarvestProcessName = "Harvest";

    public async Task<DataQualityDetectorReadModel> BuildAsync(
        DataQualityDetectorOptions settings,
        DateTime now,
        CancellationToken ct)
    {
        var queueId = (int)mainAnalysisOptions.Value.QueueId;
        // Split in half for the trend, so an odd sample size would give the two windows
        // different weights.
        var sampleSize = Math.Max(2, settings.OrphanSampleMatchesPerPlatform / 2 * 2);
        var half = sampleSize / 2;

        var samples = await LoadOrphanSampleAsync(queueId, sampleSize, half, ct);
        var trackedPlatforms = await DetectorCards.LoadTrackedPlatformsAsync(db, ct);

        var rows = new List<DataQualityDetectorRowReadModel>();
        long orphanRows = 0;

        foreach (var sample in samples.OrderBy(sample => sample.PlatformId, StringComparer.Ordinal))
        {
            var reading = DataQualityDetectorEvaluator.ReadOrphanRatio(
                sample.RecentOrphans,
                sample.RecentParticipants,
                sample.PreviousOrphans,
                sample.PreviousParticipants);

            orphanRows += sample.RecentOrphans + sample.PreviousOrphans;

            var levelStatus = DataQualityDetectorEvaluator.Classify(
                reading.Percent,
                settings.OrphanRatioAmberPercent,
                settings.OrphanRatioRedPercent);

            // The trend votes only when there is one to read and the share is already
            // high enough for a rise to mean a regression rather than noise (#1656).
            var riseStatus = DataQualityDetectorEvaluator.ClassifyOrphanRise(
                reading,
                settings.OrphanRatioRiseMinLevelPercent,
                settings.OrphanRatioRiseAmberPoints,
                settings.OrphanRatioRiseRedPoints);

            rows.Add(new DataQualityDetectorRowReadModel
            {
                Label = sample.PlatformId,
                Status = DataQualityDetectorEvaluator.Worst([levelStatus, riseStatus]).ToWireName(),
                Value = reading.Percent,
                ValueLabel = FormatPercent(reading.Percent),
                // The printed number is the level, so it carries the level's verdict:
                // a row amber on its trend used to colour the number that was fine.
                ValueStatus = levelStatus.ToWireName(),
                Note = FormatOrphanTrend(reading, sample)
            });
        }

        // A platform we track but have never sampled produces no row at all from the
        // lateral. Silence is the failure this panel exists to catch, so say it out loud.
        rows.AddRange(DetectorCards.MissingPlatformRows(
            trackedPlatforms,
            samples.Select(sample => sample.PlatformId),
            "Tracked accounts on this platform, but no ranked match to sample."));

        // Harvest is what turns orphan participants into candidates, so its silence is the
        // other half of a rising orphan share — same card, own row.
        var harvestRuns = await processRunStore.GetLatestPerProcessAsync(
            [HarvestProcessName], onlySuccesses: true, ct);
        var harvestLast = harvestRuns is [var latestHarvest, ..]
            ? (DateTime?)latestHarvest.FinishedAtUtc
            : null;
        var harvestAge = DataQualityDetectorEvaluator.AgeHours(harvestLast, now);
        var harvestStatus = DataQualityDetectorEvaluator.Classify(
            harvestAge,
            settings.HarvestStaleAmberHours,
            settings.HarvestStaleRedHours);

        rows.Add(new DataQualityDetectorRowReadModel
        {
            Label = "Harvest (last success)",
            Status = harvestStatus.ToWireName(),
            Value = harvestAge,
            ValueLabel = DataQualityDetectorEvaluator.FormatAge(harvestAge),
            Note = harvestLast is null
                ? "No successful Harvest run on record — orphan participants are not being turned into candidates."
                : "Harvest turns orphan participants into discovery candidates."
        });

        return new DataQualityDetectorReadModel
        {
            Key = "orphanParticipants",
            Title = "Orphan participants & harvest",
            Status = DataQualityDetectorEvaluator.Worst(rows.Select(DetectorCards.ParseStatus)).ToWireName(),
            Count = orphanRows,
            CountLabel = "untracked participants in the sample",
            Headline = BuildOrphanHeadline(rows),
            SourceNote = string.Create(
                CultureInfo.InvariantCulture,
                // One interpolated literal, not a concatenation: string.Create takes an
                // interpolated-string handler by ref, which a `+` expression cannot satisfy.
                $"Newest {sampleSize} ranked matches per platform (an index range on IX_matches_platform_queue_game_start), split into two windows of {half} for the trend. A high orphan share is normal — one tracked player contributes one tracked row and nine untracked ones; the anomaly is the approach to 100%. The trend is only judged once the share passes {settings.OrphanRatioRiseMinLevelPercent:0.#}%, below which a window-to-window move is sampling noise."),
            Rows = rows,
            Thresholds =
            [
                DetectorCards.Threshold("orphan share", settings.OrphanRatioAmberPercent, settings.OrphanRatioRedPercent, "percent"),
                DetectorCards.Threshold("rise vs previous window, once the share is already high", settings.OrphanRatioRiseAmberPoints, settings.OrphanRatioRiseRedPoints, "percent"),
                DetectorCards.Threshold("time since last Harvest", settings.HarvestStaleAmberHours, settings.HarvestStaleRedHours, "hours")
            ]
        };
    }

    private async Task<IReadOnlyList<OrphanSampleRow>> LoadOrphanSampleAsync(
        int queueId,
        int sampleSize,
        int half,
        CancellationToken ct)
    {
        // Platforms come from riot_accounts (a few thousand rows) rather than from a
        // DISTINCT over matches, which would be the scan this detector exists to avoid.
        // The lateral then reads exactly `sampleSize` rows per platform through
        // IX_matches_platform_queue_game_start; the row_number sits outside the LIMIT
        // because a window function inside it would be computed over the whole partition.
        FormattableString sql = $"""
            SELECT
                s."PlatformId" AS "PlatformId",
                count(*) FILTER (WHERE s.rn <= {half})::bigint AS "RecentParticipants",
                count(*) FILTER (WHERE s.rn <= {half} AND p."RiotAccountId" IS NULL)::bigint AS "RecentOrphans",
                count(*) FILTER (WHERE s.rn > {half})::bigint AS "PreviousParticipants",
                count(*) FILTER (WHERE s.rn > {half} AND p."RiotAccountId" IS NULL)::bigint AS "PreviousOrphans"
            FROM (
                SELECT platforms."PlatformId", newest."Id", row_number() OVER (
                    PARTITION BY platforms."PlatformId" ORDER BY newest."GameStartTimeUtc" DESC) AS rn
                FROM (SELECT DISTINCT "PlatformId" FROM riot_accounts) platforms
                CROSS JOIN LATERAL (
                    SELECT m."Id", m."GameStartTimeUtc"
                    FROM matches m
                    WHERE m."PlatformId" = platforms."PlatformId" AND m."QueueId" = {queueId}
                    ORDER BY m."GameStartTimeUtc" DESC
                    LIMIT {sampleSize}
                ) newest
            ) s
            JOIN match_participants p ON p."MatchId" = s."Id"
            GROUP BY s."PlatformId"
            """;

        return await db.Database.SqlQuery<OrphanSampleRow>(sql).ToListAsync(ct);
    }

    private static string? FormatPercent(double? value) => value is null
        ? null
        : string.Create(CultureInfo.InvariantCulture, $"{value.Value:F1}% orphaned");

    private static string FormatOrphanTrend(OrphanRatioReading reading, OrphanSampleRow sample)
    {
        var sampled = sample.RecentParticipants + sample.PreviousParticipants;
        var trend = reading.RisePoints is null
            ? "no comparable previous window"
            : string.Create(CultureInfo.InvariantCulture, $"{reading.RisePoints.Value:+0.0;-0.0;0.0} pts vs the previous window");

        return string.Create(CultureInfo.InvariantCulture, $"{sampled} participants sampled, {trend}.");
    }

    private static string BuildOrphanHeadline(IEnumerable<DataQualityDetectorRowReadModel> rows)
    {
        var worst = DataQualityDetectorEvaluator.Worst(rows.Select(DetectorCards.ParseStatus));

        return worst switch
        {
            DetectorStatus.Green => "Recent matches are still being attributed to tracked accounts, and Harvest is running.",
            DetectorStatus.Unknown => "Part of the orphan sample could not be measured.",
            _ => "Attribution is degrading — recent matches are increasingly untracked, or Harvest has stopped turning them into candidates."
        };
    }

    /// <summary>One platform's orphan sample, split into the newer and older window.</summary>
    private sealed record OrphanSampleRow(
        string PlatformId,
        long RecentParticipants,
        long RecentOrphans,
        long PreviousParticipants,
        long PreviousOrphans);
}
