using System.Globalization;
using Core.Lol.Patches;
using Data;
using Data.Ops.Mongo;
using Microsoft.EntityFrameworkCore;
using TrueMain.Options;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.DataQuality;

/// <summary>
/// The aggregate-freshness detector: the newest successful run per aggregation fold for
/// the card, and the per-champion breakdown — the one grouped scan of
/// <c>champion_aggregate_scopes</c> — kept behind its own on-demand endpoint.
/// </summary>
internal sealed class AggregateFreshnessDetector(TrueMainDbContext db, IProcessRunStore processRunStore)
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
        "ChampionBanAggregation",
        "ChampionSynergyAggregation", "ChampionProfileAggregation", "ChampionItemContextAggregation"
    ];

    public async Task<DataQualityDetectorReadModel> BuildAsync(
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

        var freshnessStatus = DataQualityDetectorEvaluator.Worst(rows.Select(DetectorCards.ParseStatus));

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
                DetectorCards.Threshold("time since last success", settings.AggregationStaleAmberHours, settings.AggregationStaleRedHours, "hours")
            ],
            HasDrillDownEndpoint = true
        };
    }

    /// <summary>The on-demand per-champion breakdown behind the card's drill-down.</summary>
    public async Task<AggregateFreshnessReadModel> GetBreakdownAsync(
        DataQualityDetectorOptions settings,
        DateTime now,
        CancellationToken ct)
    {
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
}
