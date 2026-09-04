using System.Globalization;
using Core.Lol.Patches;
using Core.Options;
using Data;
using Data.DataQuality;
using Data.Entities;
using Data.Ops.Mongo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// Measures the data-quality detectors (#924). The judgement lives in the pure
/// <see cref="DataQualityDetectorEvaluator"/>; this class only asks the database the
/// questions and words the answers.
///
/// <para>
/// <b>Cost.</b> The panel loads on a page view, so no detector is allowed a scan it
/// cannot afford. The duplicate detector groups the <c>champion_dim_*</c> tables (tens
/// of thousands of rows), the orphan detector samples the newest matches per platform
/// through <c>IX_matches_platform_queue_game_start</c> rather than ratioing the whole
/// <c>match_participants</c> table, and the per-champion freshness breakdown — the one
/// genuinely grouped scan — is a separate endpoint behind an explicit click, the same
/// split #925 made for storage. The remaining counts are of the same order as the ones
/// <see cref="OverviewQueryService"/> already runs on the overview panel.
/// </para>
///
/// <para>
/// <b>Failure is not a pass.</b> Each detector is measured independently and a query
/// that throws yields <c>unknown</c> with the reason attached, never green and never a
/// 500 for the whole panel: one broken detector must not blind the other four.
/// </para>
/// </summary>
public sealed class DataQualityDetectorsQueryService(
    TrueMainDbContext db,
    IProcessRunStore processRunStore,
    IOptions<DataQualityDetectorOptions> options,
    IOptions<MainAnalysisOptions> mainAnalysisOptions,
    TimeProvider timeProvider,
    ILogger<DataQualityDetectorsQueryService> logger) : IDataQualityDetectorsQueryService
{
    /// <summary>
    /// The folds whose freshness the panel judges. Deliberately only the aggregations:
    /// the ingestion processes have their own detector (newest match per platform), and
    /// a process that legitimately runs rarely would sit permanently amber here.
    /// </summary>
    private static readonly string[] AggregationProcessNames =
    [
        "ChampionPatternAggregation",
        "ChampionMatchupLeadAggregation",
        "ChampionPowerspikeAggregation",
        "ChampionBanAggregation",
        "ChampionSynergyAggregation", "ChampionProfileAggregation", "ChampionItemContextAggregation"
    ];

    private const string HarvestProcessName = "Harvest";

    public async Task<DataQualityDetectorsReadModel> GetDetectorsAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var settings = options.Value;

        var detectors = new List<DataQualityDetectorReadModel>
        {
            await SafeAsync(
                "duplicateDimensionRows",
                "Duplicate dimension rows",
                () => BuildDuplicateDimensionsAsync(settings, ct),
                ct),
            await SafeAsync(
                "aggregateFreshness",
                "Aggregate freshness",
                () => BuildAggregateFreshnessAsync(settings, now, ct),
                ct),
            await SafeAsync(
                "orphanParticipants",
                "Orphan participants & harvest",
                () => BuildOrphanParticipantsAsync(settings, now, ct),
                ct),
            await SafeAsync(
                "ingestionLag",
                "Ingestion lag & queues",
                () => BuildIngestionLagAsync(settings, now, ct),
                ct),
            await SafeAsync(
                "rowSanity",
                "Row-level sanity",
                () => BuildRowSanityAsync(settings, ct),
                ct)
        };

        return new DataQualityDetectorsReadModel
        {
            Detectors = detectors,
            EvaluatedAtUtc = now
        };
    }

    public async Task<AggregateFreshnessReadModel> GetAggregateFreshnessAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var settings = options.Value;
        var patchCount = Math.Max(1, settings.FreshnessPatchCount);
        var championLimit = Math.Max(1, settings.FreshnessChampionLimit);

        // Every stored GameVersion, indexed by the patch it normalises onto. Newest
        // patches first: only these are judged, because older ones are frozen by design
        // (#466) and can never be refreshed, so reporting them as stale is noise that
        // never clears.
        var storedVersions = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Select(scope => scope.GameVersion)
            .Distinct()
            .ToListAsync(ct);

        var versionsByPatch = storedVersions
            .Where(version => PatchVersion.TryParse(version, out _))
            .GroupBy(PatchVersion.Normalize, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var newestPatches = versionsByPatch.Keys
            .OrderByDescending(PatchVersion.Parse)
            .Take(patchCount)
            .ToList();

        if (newestPatches.Count == 0)
        {
            return new AggregateFreshnessReadModel
            {
                StaleAfterHours = settings.AggregationStaleAmberHours,
                EvaluatedAtUtc = now
            };
        }

        // Filter on the values the column actually holds, resolved above, rather than on
        // a shape we assume it has. The aggregation normalises on write, so production
        // stores "16.15" — matching `StartsWith("16.15.")` selected nothing at all and
        // silently emptied the whole breakdown. Going through the stored values keeps the
        // filter correct whichever form a row was written in.
        var scopeVersions = newestPatches.SelectMany(patch => versionsByPatch[patch]).ToList();

        var rows = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scopeVersions.Contains(scope.GameVersion))
            .GroupBy(scope => new { scope.ChampionId, scope.GameVersion })
            .Select(group => new
            {
                group.Key.ChampionId,
                group.Key.GameVersion,
                LastAggregatedAtUtc = group.Max(scope => scope.AggregatedAtUtc),
                ScopeRows = group.LongCount()
            })
            .ToListAsync(ct);

        // One reading per champion and patch, collapsing the raw GameVersion values that
        // normalise onto the same patch.
        var champions = rows
            .GroupBy(row => new { row.ChampionId, Patch = PatchVersion.Normalize(row.GameVersion) })
            .Select(group =>
            {
                var last = group.Max(row => row.LastAggregatedAtUtc);
                var age = DataQualityDetectorEvaluator.AgeHours(last, now) ?? 0;

                return new ChampionFreshnessRowReadModel
                {
                    ChampionId = group.Key.ChampionId,
                    Patch = group.Key.Patch,
                    LastAggregatedAtUtc = last,
                    AgeHours = age,
                    ScopeRows = group.Sum(row => row.ScopeRows),
                    Status = DataQualityDetectorEvaluator
                        .Classify(age, settings.AggregationStaleAmberHours, settings.AggregationStaleRedHours)
                        .ToWireName()
                };
            })
            .OrderByDescending(row => row.AgeHours)
            .ThenBy(row => row.ChampionId)
            .ToList();

        return new AggregateFreshnessReadModel
        {
            Patches = newestPatches,
            Champions = [.. champions.Take(championLimit)],
            ChampionCount = champions.Select(row => row.ChampionId).Distinct().Count(),
            StaleChampionCount = champions.Count(row => row.AgeHours >= settings.AggregationStaleAmberHours),
            StaleAfterHours = settings.AggregationStaleAmberHours,
            EvaluatedAtUtc = now
        };
    }

    // ---- detectors -----------------------------------------------------------

    private async Task<DataQualityDetectorReadModel> BuildDuplicateDimensionsAsync(
        DataQualityDetectorOptions settings,
        CancellationToken ct)
    {
        var rows = new List<DataQualityDetectorRowReadModel>();
        long duplicateGroups = 0;
        long nonCanonicalRows = 0;

        foreach (var audit in ChampionDimensionCanonicalKeys.AuditedTables)
        {
            var duplicates = await CountDuplicateGroupsAsync(audit, ct);
            var nonCanonical = audit.NonCanonicalPredicate is null
                ? (long?)null
                : await CountNonCanonicalAsync(audit, ct);

            duplicateGroups += duplicates;
            nonCanonicalRows += nonCanonical ?? 0;

            var duplicateStatus = DataQualityDetectorEvaluator.Classify(
                duplicates,
                settings.DuplicateDimensionGroupsAmber,
                settings.DuplicateDimensionGroupsRed);

            // The leading indicator only votes when it exists. Unknown means "the answer
            // could not be measured", and the answer here is the duplicate count — which
            // was measured. A signal that is *permanently* inapplicable by design (a
            // starter basket's key is generated by Postgres, so no stored order carries
            // identity) would otherwise pin that row to unknown for ever, which teaches
            // the operator to ignore the colour.
            var statuses = nonCanonical is null
                ? new[] { duplicateStatus }
                :
                [
                    duplicateStatus,
                    DataQualityDetectorEvaluator.Classify(
                        nonCanonical,
                        settings.NonCanonicalDimensionRowsAmber,
                        settings.NonCanonicalDimensionRowsRed)
                ];

            var note = nonCanonical is null
                // Inapplicable, not unchecked: the row has no stored order to be out of.
                ? audit.Rationale
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{audit.Rationale} {nonCanonical} row(s) stored outside canonical order.");

            rows.Add(new DataQualityDetectorRowReadModel
            {
                Label = audit.TableName,
                Status = DataQualityDetectorEvaluator.Worst(statuses).ToWireName(),
                Value = duplicates,
                ValueLabel = string.Create(CultureInfo.InvariantCulture, $"{duplicates} duplicate group(s)"),
                Note = note
            });
        }

        var exempt = string.Join(
            " ",
            ChampionDimensionCanonicalKeys.ExemptTables.Select(table => $"{table.TableName}: {table.Reason}"));

        var duplicateCardStatus = DataQualityDetectorEvaluator.Worst(rows.Select(ParseStatus));

        return new DataQualityDetectorReadModel
        {
            Key = "duplicateDimensionRows",
            Title = "Duplicate dimension rows",
            Status = duplicateCardStatus.ToWireName(),
            Count = duplicateGroups,
            CountLabel = "canonical-key groups holding more than one row",
            // Worded from the verdict. A table holding rows outside canonical order but
            // no duplicate yet turns this card amber, and a headline computed from the
            // duplicate count alone would pair that badge with "every row is unique".
            Headline = duplicateCardStatus switch
            {
                DetectorStatus.Green => "Every audited dimension row is unique under its canonical key.",
                DetectorStatus.Unknown => "Part of the dimension audit could not be measured.",
                _ when duplicateGroups > 0 => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{duplicateGroups} canonical-key group(s) hold more than one row — those games are split across rows, the #911 failure. The schema forbids this, so a constraint is missing."),
                _ => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{nonCanonicalRows} row(s) are stored outside canonical order — nothing is split yet, but the reader's canonical lookup will miss them and mint a second row (#911). The schema forbids this, so a constraint is missing."),
            },
            SourceNote = "Groups each champion_dim_* table on the same canonical key its UNIQUE index and "
                + "CHECK are built from (#1418), so a count above zero means a constraint went missing "
                + "rather than a repair being owed. Not audited — " + exempt,
            Rows = rows,
            Thresholds =
            [
                Threshold("duplicate groups", settings.DuplicateDimensionGroupsAmber, settings.DuplicateDimensionGroupsRed, "count"),
                Threshold("non-canonical rows", settings.NonCanonicalDimensionRowsAmber, settings.NonCanonicalDimensionRowsRed, "count")
            ]
        };
    }

    private async Task<DataQualityDetectorReadModel> BuildAggregateFreshnessAsync(
        DataQualityDetectorOptions settings,
        DateTime now,
        CancellationToken ct)
    {
        // The newest successful run per fold, from the Mongo process-run store
        // (moved off Postgres with the rest of the admin observability data).
        var lastSuccesses = await processRunStore.GetLatestPerProcessAsync(
            AggregationProcessNames, onlySuccesses: true, ct);

        var byProcess = lastSuccesses.ToDictionary(
            run => run.ProcessName,
            run => run.FinishedAtUtc,
            StringComparer.Ordinal);

        var rows = new List<DataQualityDetectorRowReadModel>();
        long stale = 0;

        foreach (var processName in AggregationProcessNames)
        {
            var last = byProcess.TryGetValue(processName, out var value) ? value : (DateTime?)null;
            var age = DataQualityDetectorEvaluator.AgeHours(last, now);
            var status = DataQualityDetectorEvaluator.Classify(
                age,
                settings.AggregationStaleAmberHours,
                settings.AggregationStaleRedHours);

            if (status is DetectorStatus.Amber or DetectorStatus.Red)
            {
                stale++;
            }

            rows.Add(new DataQualityDetectorRowReadModel
            {
                Label = processName,
                Status = status.ToWireName(),
                Value = age,
                ValueLabel = DataQualityDetectorEvaluator.FormatAge(age),
                Note = last is null
                    ? "No successful run on record — the fold has never completed, or its runs predate process_runs."
                    : null
            });
        }

        var freshnessStatus = DataQualityDetectorEvaluator.Worst(rows.Select(ParseStatus));

        return new DataQualityDetectorReadModel
        {
            Key = "aggregateFreshness",
            Title = "Aggregate freshness",
            Status = freshnessStatus.ToWireName(),
            Count = stale,
            CountLabel = "aggregations past their staleness line",
            // Worded from the verdict, not from the count: a card that says "every
            // aggregation completed" while reading unknown is the dashboard lying, and
            // that is exactly what an empty process_runs table produces.
            Headline = freshnessStatus switch
            {
                DetectorStatus.Green => "Every aggregation completed within its expected cadence.",
                DetectorStatus.Unknown => "Some aggregations have never recorded a successful run, so their freshness is unmeasured.",
                _ => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{stale} aggregation(s) have not completed recently — champion pages are serving numbers older than the matches behind them.")
            },
            SourceNote = "Last successful run per aggregation from process_runs (one grouped read of a small table). "
                + "The per-champion breakdown is a separate on-demand endpoint because it needs a grouped scan of champion_aggregate_scopes.",
            Rows = rows,
            Thresholds =
            [
                Threshold("time since last success", settings.AggregationStaleAmberHours, settings.AggregationStaleRedHours, "hours")
            ],
            HasDrillDownEndpoint = true
        };
    }

    private async Task<DataQualityDetectorReadModel> BuildOrphanParticipantsAsync(
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
        var trackedPlatforms = await LoadTrackedPlatformsAsync(ct);

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

            // As with the duplicate card: the trend votes only when there is one. A
            // platform with fewer matches than the sample window has no previous half to
            // compare against, and that is a thin corpus, not an unmeasured level.
            var statuses = reading.RisePoints is null
                ? new[] { levelStatus }
                :
                [
                    levelStatus,
                    DataQualityDetectorEvaluator.Classify(
                        reading.RisePoints,
                        settings.OrphanRatioRiseAmberPoints,
                        settings.OrphanRatioRiseRedPoints)
                ];

            rows.Add(new DataQualityDetectorRowReadModel
            {
                Label = sample.PlatformId,
                Status = DataQualityDetectorEvaluator.Worst(statuses).ToWireName(),
                Value = reading.Percent,
                ValueLabel = FormatPercent(reading.Percent),
                Note = FormatOrphanTrend(reading, sample)
            });
        }

        // A platform we track but have never sampled produces no row at all from the
        // lateral. Silence is the failure this panel exists to catch, so say it out loud.
        rows.AddRange(MissingPlatformRows(
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
            Status = DataQualityDetectorEvaluator.Worst(rows.Select(ParseStatus)).ToWireName(),
            Count = orphanRows,
            CountLabel = "untracked participants in the sample",
            Headline = BuildOrphanHeadline(rows),
            SourceNote = string.Create(
                CultureInfo.InvariantCulture,
                // One interpolated literal, not a concatenation: string.Create takes an
                // interpolated-string handler by ref, which a `+` expression cannot satisfy.
                $"Newest {sampleSize} ranked matches per platform (an index range on IX_matches_platform_queue_game_start), split into two windows of {half} for the trend. A high orphan share is normal — one tracked player contributes one tracked row and nine untracked ones; the anomaly is the approach to 100%."),
            Rows = rows,
            Thresholds =
            [
                Threshold("orphan share", settings.OrphanRatioAmberPercent, settings.OrphanRatioRedPercent, "percent"),
                Threshold("rise vs previous window", settings.OrphanRatioRiseAmberPoints, settings.OrphanRatioRiseRedPoints, "percent"),
                Threshold("time since last Harvest", settings.HarvestStaleAmberHours, settings.HarvestStaleRedHours, "hours")
            ]
        };
    }

    private async Task<DataQualityDetectorReadModel> BuildIngestionLagAsync(
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
        rows.AddRange(MissingPlatformRows(
            await LoadTrackedPlatformsAsync(ct),
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

        var lagStatus = DataQualityDetectorEvaluator.Worst(rows.Select(ParseStatus));

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
                Threshold("newest match age", settings.IngestionLagAmberHours, settings.IngestionLagRedHours, "hours"),
                Threshold("matches awaiting a timeline", settings.PendingTimelineAmber, settings.PendingTimelineRed, "count"),
                Threshold("candidates queued", settings.QueuedCandidatesAmber, settings.QueuedCandidatesRed, "count"),
                Threshold("candidates processing", settings.ProcessingCandidatesAmber, settings.ProcessingCandidatesRed, "count")
            ]
        };
    }

    private async Task<DataQualityDetectorReadModel> BuildRowSanityAsync(
        DataQualityDetectorOptions settings,
        CancellationToken ct)
    {
        // Impossible rows: not "unlikely", but arithmetically impossible. One is a fold
        // bug, which is why the default threshold is 1.
        var scopesInconsistent = await db.ChampionAggregateScopes
            .AsNoTracking()
            .LongCountAsync(scope => scope.Wins > scope.Games || scope.Wins < 0 || scope.Games < 0, ct);

        var matchupsInconsistent = await db.ChampionMatchupStats
            .AsNoTracking()
            .LongCountAsync(
                stat => stat.Wins > stat.Games
                    || stat.LaneGames > stat.Games
                    || stat.LaneWins + stat.LaneLosses > stat.LaneGames
                    // The gold gap is summed over a subset of the judged lanes — behind
                    // on rows folded before #976, never ahead of them.
                    || stat.LaneGoldDiffGames > stat.LaneGames,
                ct);

        // A champion cannot be banned more often than there were matches to ban it in.
        var bansInconsistent = await db.ChampionBanStats
            .AsNoTracking()
            .Join(
                db.BanScopeTotals.AsNoTracking(),
                ban => new { ban.Patch, ban.EloBracket },
                total => new { total.Patch, total.EloBracket },
                (ban, total) => new { ban.Bans, total.Matches })
            .LongCountAsync(row => row.Bans > row.Matches, ct);

        var zeroSampleScopes = await db.ChampionAggregateScopes
            .AsNoTracking()
            .LongCountAsync(scope => scope.Games <= 0, ct);

        var inconsistent = scopesInconsistent + matchupsInconsistent + bansInconsistent;

        var rows = new List<DataQualityDetectorRowReadModel>
        {
            SanityRow(
                "champion_aggregate_scopes — impossible totals",
                scopesInconsistent,
                settings.InconsistentAggregateRowsAmber,
                settings.InconsistentAggregateRowsRed,
                "Wins above games, or a negative total."),
            SanityRow(
                "champion_matchup_stats — impossible totals",
                matchupsInconsistent,
                settings.InconsistentAggregateRowsAmber,
                settings.InconsistentAggregateRowsRed,
                "Wins above games, lane games above games, or lane outcomes above lane games (#919)."),
            SanityRow(
                "champion_ban_stats — bans above their denominator",
                bansInconsistent,
                settings.InconsistentAggregateRowsAmber,
                settings.InconsistentAggregateRowsRed,
                "More bans than there were matches in the same patch and bracket (#920)."),
            SanityRow(
                "champion_aggregate_scopes — zero-sample rows",
                zeroSampleScopes,
                settings.ZeroSampleAggregateRowsAmber,
                settings.ZeroSampleAggregateRowsRed,
                "Harmless to a reader (a sample floor hides them) but still a row that should never have been written.")
        };

        // The sanity counts always vote. The patch-volume rows vote only where a patch
        // was actually judged: the newest and oldest are deliberately not comparable, and
        // letting them vote would pin this card to unknown on every healthy corpus.
        var sanityStatuses = rows.Select(ParseStatus).ToList();
        var patchRows = await BuildPatchVolumeRowsAsync(settings, ct);
        rows.AddRange(patchRows);
        sanityStatuses.AddRange(patchRows
            .Select(ParseStatus)
            .Where(status => status is not DetectorStatus.Unknown));

        var thinPatches = patchRows.Count(row => ParseStatus(row) is DetectorStatus.Amber or DetectorStatus.Red);
        var sanityCardStatus = DataQualityDetectorEvaluator.Worst(sanityStatuses);

        return new DataQualityDetectorReadModel
        {
            Key = "rowSanity",
            Title = "Row-level sanity",
            Status = sanityCardStatus.ToWireName(),
            Count = inconsistent,
            CountLabel = "arithmetically impossible aggregate rows",
            // Same rule as every other card: worded from the verdict. Zero-sample rows
            // and thin patches raise this card on their own, and a headline computed from
            // the impossible-row count alone would answer them with "nothing contradicts
            // its own totals" — true, and beside the point the badge is making.
            Headline = sanityCardStatus switch
            {
                DetectorStatus.Green => "No aggregate row contradicts its own totals.",
                DetectorStatus.Unknown => "Part of the row-level audit could not be measured.",
                _ when inconsistent > 0 => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{inconsistent} aggregate row(s) contradict their own totals — a fold is writing numbers it cannot have measured."),
                _ when thinPatches > 0 => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{thinPatches} patch(es) hold far fewer matches than the median comparable patch — ingestion was down, or is still behind."),
                _ => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{zeroSampleScopes} aggregate row(s) carry no games — harmless to a reader, but rows that should never have been written."),
            },
            SourceNote = "Predicate counts over the aggregate tables, plus per-patch match volumes against the median "
                + "of the comparable patches. The newest and oldest patch are never judged: one is still filling, the "
                + "other is being retention-trimmed.",
            Rows = rows,
            Thresholds =
            [
                Threshold("impossible rows", settings.InconsistentAggregateRowsAmber, settings.InconsistentAggregateRowsRed, "count"),
                Threshold("zero-sample rows", settings.ZeroSampleAggregateRowsAmber, settings.ZeroSampleAggregateRowsRed, "count"),
                // A floor, not a ceiling: a patch is anomalous when its match count falls
                // *below* this share of the median patch.
                Threshold("patch volume vs median", settings.PatchVolumeAnomalyRatio, 0, "ratio", "below")
            ]
        };
    }

    private async Task<IReadOnlyList<DataQualityDetectorRowReadModel>> BuildPatchVolumeRowsAsync(
        DataQualityDetectorOptions settings,
        CancellationToken ct)
    {
        var queueId = (int)mainAnalysisOptions.Value.QueueId;

        var rawCounts = await db.Matches
            .AsNoTracking()
            .Where(match => match.QueueId == queueId)
            .GroupBy(match => match.GameVersion)
            .Select(group => new { GameVersion = group.Key, Matches = group.LongCount() })
            .ToListAsync(ct);

        var volumes = rawCounts
            .Select(row => new { Patch = PatchVersion.Normalize(row.GameVersion), row.Matches })
            .Where(row => PatchVersion.TryParse(row.Patch, out _))
            .GroupBy(row => row.Patch, StringComparer.Ordinal)
            .Select(group => new PatchVolume(group.Key, group.Sum(row => row.Matches)))
            .OrderBy(volume => PatchVersion.Parse(volume.Patch))
            .ToList();

        var reading = DataQualityDetectorEvaluator.ReadPatchVolumes(
            volumes,
            settings.PatchVolumeAnomalyRatio,
            settings.PatchVolumeMinPatches);

        return
        [
            .. reading.Verdicts
                .OrderByDescending(verdict => PatchVersion.Parse(verdict.Patch.Patch))
                .Select(verdict => new DataQualityDetectorRowReadModel
                {
                    Label = string.Create(CultureInfo.InvariantCulture, $"patch {verdict.Patch.Patch}"),
                    Status = (verdict.Judged
                        ? verdict.Thin ? DetectorStatus.Amber : DetectorStatus.Green
                        // Unjudged is not a pass: the edge patches are simply not
                        // comparable, and saying so beats a green that means nothing.
                        : DetectorStatus.Unknown).ToWireName(),
                    Value = verdict.Patch.Matches,
                    ValueLabel = string.Create(CultureInfo.InvariantCulture, $"{verdict.Patch.Matches} matches"),
                    Note = verdict.Judged
                        ? reading.MedianMatches is null
                            ? null
                            : string.Create(
                                CultureInfo.InvariantCulture,
                                $"Median of the comparable patches: {reading.MedianMatches:F0} matches.")
                        : "Edge patch — still filling, or being trimmed by retention. Not comparable."
                })
        ];
    }

    // ---- measurement helpers -------------------------------------------------

    private async Task<long> CountDuplicateGroupsAsync(ChampionDimensionAudit audit, CancellationToken ct)
    {
        // The SQL fragments are compile-time constants owned by ChampionDimensionCanonicalKeys,
        // never user input, so raw interpolation is safe here — and necessary, since a
        // GROUP BY expression list cannot be a parameter.
        var sql = $"""
            SELECT count(*)::bigint AS "Value"
            FROM (
                SELECT 1
                FROM {audit.FromSql}
                GROUP BY {audit.CanonicalKeyExpression}
                HAVING count(*) > 1
            ) duplicate_groups
            """;

        return await db.Database.SqlQueryRaw<long>(sql).SingleAsync(ct);
    }

    private async Task<long> CountNonCanonicalAsync(ChampionDimensionAudit audit, CancellationToken ct)
    {
        var sql = $"""
            SELECT count(*)::bigint AS "Value"
            FROM {audit.TableName}
            WHERE {audit.NonCanonicalPredicate}
            """;

        return await db.Database.SqlQueryRaw<long>(sql).SingleAsync(ct);
    }

    /// <summary>
    /// Platforms we track accounts on. Read from <c>riot_accounts</c> (a few thousand
    /// rows) rather than from a DISTINCT over <c>matches</c>, which would be the scan
    /// these detectors exist to avoid.
    /// </summary>
    private async Task<IReadOnlyList<string>> LoadTrackedPlatformsAsync(CancellationToken ct)
        => await db.RiotAccounts
            .AsNoTracking()
            .Select(account => account.PlatformId)
            .Distinct()
            .ToListAsync(ct);

    /// <summary>
    /// An <c>unknown</c> row for every tracked platform absent from a measurement. A
    /// platform that simply drops out of a GROUP BY reads as "nothing to report", which
    /// is indistinguishable from "healthy" on a card — and it is the opposite.
    /// </summary>
    private static IEnumerable<DataQualityDetectorRowReadModel> MissingPlatformRows(
        IEnumerable<string> trackedPlatforms,
        IEnumerable<string> measuredPlatforms,
        string note)
    {
        var measured = measuredPlatforms.ToHashSet(StringComparer.Ordinal);

        return trackedPlatforms
            .Where(platform => !measured.Contains(platform))
            .OrderBy(platform => platform, StringComparer.Ordinal)
            .Select(platform => new DataQualityDetectorRowReadModel
            {
                Label = platform,
                Status = DetectorStatus.Unknown.ToWireName(),
                Note = note
            });
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

    // ---- shaping helpers -----------------------------------------------------

    private async Task<DataQualityDetectorReadModel> SafeAsync(
        string key,
        string title,
        Func<Task<DataQualityDetectorReadModel>> build,
        CancellationToken ct)
    {
        try
        {
            return await build();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One detector's broken query must not blind the other four, and must not
            // read as green either.
            logger.LogWarning(ex, "Data-quality detector {Detector} failed to measure", key);

            return new DataQualityDetectorReadModel
            {
                Key = key,
                Title = title,
                Status = DetectorStatus.Unknown.ToWireName(),
                CountLabel = string.Empty,
                Headline = "This detector could not be measured.",
                UnknownReason = ex.Message,
                SourceNote = "The measurement failed; the panel reports unknown rather than a pass."
            };
        }
        finally
        {
            ct.ThrowIfCancellationRequested();
        }
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

    private static DataQualityDetectorRowReadModel SanityRow(
        string label,
        long value,
        long amber,
        long red,
        string note) => new()
        {
            Label = label,
            Status = DataQualityDetectorEvaluator.Classify(value, amber, red).ToWireName(),
            Value = value,
            ValueLabel = string.Create(CultureInfo.InvariantCulture, $"{value} row(s)"),
            Note = note
        };

    private static DataQualityThresholdReadModel Threshold(
        string label,
        double amber,
        double red,
        string unit,
        string direction = "above") => new()
    {
        Label = label,
        // A level of 0 or less is disabled, and the panel should omit it rather than
        // print a level of "0" that nothing can ever trip.
        Amber = amber > 0 ? amber : null,
        Red = red > 0 ? red : null,
        Unit = unit,
        Direction = direction
    };

    private static DetectorStatus ParseStatus(DataQualityDetectorRowReadModel row) => row.Status switch
    {
        "green" => DetectorStatus.Green,
        "amber" => DetectorStatus.Amber,
        "red" => DetectorStatus.Red,
        _ => DetectorStatus.Unknown
    };

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
        var worst = DataQualityDetectorEvaluator.Worst(rows.Select(ParseStatus));

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
