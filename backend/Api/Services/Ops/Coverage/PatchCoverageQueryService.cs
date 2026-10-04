using Core.Lol.Patches;
using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.DataQuality;

namespace TrueMain.Services.Ops.Coverage;

/// <summary>
/// Answers "is the current patch servable?" for the admin patch-coverage view (#1033).
/// </summary>
public interface IPatchCoverageQueryService
{
    /// <summary>
    /// Ingestion, aggregate coverage against the public games floor, and per-fold state
    /// for the newest <c>PatchCoverage:PatchCount</c> patches.
    /// </summary>
    Task<PatchCoverageReadModel> GetAsync(CancellationToken ct);
}

/// <summary>
/// Answers "is the current patch servable?" (#1033).
///
/// <para>
/// <b>Why the numbers here must mirror the public read exactly.</b> The coverage figure
/// groups <c>champion_aggregate_scopes</c> by <c>(champion, lane)</c> on the configured
/// queue, drops lane-less rows and compares the summed games against
/// <c>ChampionsList:MinSampleGames</c> — the same grain, the same filter and the same
/// floor <see cref="Champions.Directory.ChampionSummariesQueryService"/> applies. Anything else
/// would produce a page that confidently reports on a bar the site does not enforce.
/// </para>
///
/// <para>
/// <b>Cost.</b> None of the fold tables is indexed on its patch column, so every
/// per-patch rollup is a grouped scan. That is affordable exactly once, behind an
/// explicit navigation — the same trade the per-champion freshness drill-down makes
/// (#925) — and never on the overview. Each fold is measured in isolation, so one slow
/// or broken table yields <c>unknown</c> with the reason attached rather than a 500 for
/// the whole page: a fold that cannot be measured is not a fold that is empty.
/// </para>
///
/// <para>
/// <b>Layout.</b> The two per-patch reads live in <see cref="PatchCoverageMeasurements"/>,
/// the fold rollups in <see cref="PatchFoldMeasurements"/>, the judgement in the pure
/// <see cref="PatchCoverageEvaluator"/> and the read-model assembly in the equally pure
/// <see cref="PatchCoverageRows"/>; this class only sequences them and contains their failures.
/// </para>
/// </summary>
public sealed class PatchCoverageQueryService(
    TrueMainDbContext db,
    IOptions<PatchCoverageOptions> options,
    IOptions<ChampionsListOptions> championsListOptions,
    IOptions<DataQualityDetectorOptions> detectorOptions,
    IOptions<MainAnalysisOptions> mainAnalysisOptions,
    TimeProvider timeProvider,
    ILogger<PatchCoverageQueryService> logger) : IPatchCoverageQueryService
{
    private readonly PatchCoverageMeasurements _measurements = new(db);
    private readonly PatchFoldMeasurements _folds = new(db, logger);

    public async Task<PatchCoverageReadModel> GetAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var settings = options.Value;
        var queueId = (int)mainAnalysisOptions.Value.QueueId;
        var floor = Math.Max(0, championsListOptions.Value.MinSampleGames);
        var patchCount = Math.Max(1, settings.PatchCount);

        // Every stored GameVersion on the aggregate side, indexed by the patch it
        // normalises onto. The aggregation normalises on write, so production holds
        // "16.15" — but a value written in any other form still belongs to the same
        // patch, and filtering on an assumed shape rather than on the stored values is
        // what silently empties a breakdown.
        var scopeVersions = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.QueueId == queueId)
            .Select(scope => scope.GameVersion)
            .Distinct()
            .ToListAsync(ct);

        var scopeVersionsByPatch = scopeVersions
            .Where(version => PatchVersion.TryParse(version, out _))
            .GroupBy(PatchVersion.Normalize, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var matchVersions = await db.Matches
            .AsNoTracking()
            .Where(match => match.QueueId == queueId)
            .Select(match => match.GameVersion)
            .Distinct()
            .ToListAsync(ct);

        // The union, not just the aggregate side: a patch whose matches have landed but
        // whose folds have not run yet is precisely the state this page exists to name,
        // and it has no scope row to be found by.
        var coveredPatches = matchVersions
            .Concat(scopeVersions)
            .Where(version => PatchVersion.TryParse(version, out _))
            .Select(PatchVersion.Normalize)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(PatchVersion.Parse)
            .Take(patchCount)
            .ToList();

        // What the public reads actually resolve to: the newest patch holding an
        // aggregate row, exactly as ChampionAggregateScopeResolver picks it. Deliberately
        // not "the newest patch ingested" — those two diverge for the whole window
        // between a patch's first match and its first fold, which is the window this page
        // is about.
        var currentPatch = scopeVersionsByPatch.Keys
            .OrderByDescending(PatchVersion.Parse)
            .FirstOrDefault();

        if (coveredPatches.Count == 0)
        {
            return new PatchCoverageReadModel
            {
                QueueId = queueId,
                MinSampleGames = floor,
                FloorNote = PatchCoverageRows.FloorNote(floor),
                Verdict = "unknown",
                Status = DetectorStatus.Unknown.ToWireName(),
                Headline = "No match and no aggregate row carries a usable patch, so there is nothing to judge.",
                SourceNote = PatchCoverageRows.SourceNote,
                EvaluatedAtUtc = now
            };
        }

        var patchArray = coveredPatches.ToArray();

        var ingestion = await SafeAsync("ingestion", () => _measurements.LoadIngestionAsync(queueId, patchArray, ct), ct);
        var coverage = await SafeAsync(
            "coverage",
            () => _measurements.LoadCoverageAsync(queueId, coveredPatches, scopeVersionsByPatch, floor, settings, ct),
            ct);

        // Unlike a single fold, these two are the page's question. Without the ingestion
        // counts an unaggregated patch is indistinguishable from an empty one, and without
        // the coverage rollup a thin patch is indistinguishable from an unaggregated one —
        // so a failure here has to read as "not measured", never as either of the answers
        // it could no longer tell apart.
        if (ingestion.Error is not null || coverage.Error is not null)
        {
            return new PatchCoverageReadModel
            {
                QueueId = queueId,
                MinSampleGames = floor,
                FloorNote = PatchCoverageRows.FloorNote(floor),
                CurrentPatch = currentPatch,
                Verdict = "unknown",
                Status = DetectorStatus.Unknown.ToWireName(),
                Headline = "Patch coverage could not be measured, so no patch has a verdict.",
                UnknownReason = ingestion.Error ?? coverage.Error,
                SourceNote = PatchCoverageRows.SourceNote,
                EvaluatedAtUtc = now
            };
        }

        var folds = await _folds.LoadAsync(coverage.Value, ct);

        // The bar every patch is judged against, taken from the patches strictly OLDER
        // than the one being served. Those are the settled ones: the served patch and
        // anything newer are still filling, and letting a filling patch into its own
        // reference drags the bar down to whatever it happens to be, which is how a
        // coverage check comes out green on an empty patch. Same "the edge patch is not
        // comparable" rule the patch-volume detector applies (#924).
        var settled = coveredPatches
            .Where(patch => currentPatch is null
                || (PatchVersion.TryParse(patch, out var candidate)
                    && PatchVersion.TryParse(currentPatch, out var served)
                    && candidate < served))
            .Select(patch => coverage.Value.GetValueOrDefault(patch))
            .Where(value => value is { Lines: > 0 })
            .Select(value => value!.LinesPastFloor)
            .ToList();

        var bar = PatchCoverageEvaluator.ReadBar(
            settled,
            settings.ServableLinesRatio,
            settings.ServableLinesMinimum,
            currentPatch);

        var rows = coveredPatches
            .Select(patch => PatchCoverageRows.BuildPatchRow(
                patch,
                patch == currentPatch,
                ingestion.Value.GetValueOrDefault(patch),
                coverage.Value.GetValueOrDefault(patch),
                folds,
                bar,
                floor,
                detectorOptions.Value,
                now))
            .ToList();

        var current = rows.FirstOrDefault(row => row.IsCurrent);
        var newestIngested = rows[0];

        return new PatchCoverageReadModel
        {
            QueueId = queueId,
            MinSampleGames = floor,
            FloorNote = PatchCoverageRows.FloorNote(floor),
            CurrentPatch = currentPatch,
            Verdict = current?.Verdict ?? "unknown",
            Status = current?.Status ?? DetectorStatus.Unknown.ToWireName(),
            Headline = PatchCoverageRows.BuildHeadline(current, newestIngested),
            Patches = rows,
            SourceNote = PatchCoverageRows.SourceNote,
            EvaluatedAtUtc = now
        };
    }

    /// <summary>
    /// Runs one measurement, keeping any failure as a <em>reason</em> rather than turning
    /// it into an empty result. An empty dictionary and a failed query produce the same
    /// zeros, and the caller has to be able to tell them apart.
    /// </summary>
    private async Task<Measured<IReadOnlyDictionary<string, T>>> SafeAsync<T>(
        string what,
        Func<Task<IReadOnlyDictionary<string, T>>> measure,
        CancellationToken ct)
    {
        try
        {
            return new Measured<IReadOnlyDictionary<string, T>>(await measure(), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Patch-coverage measurement {Measurement} failed", what);
            return new Measured<IReadOnlyDictionary<string, T>>(
                new Dictionary<string, T>(StringComparer.Ordinal),
                ex.Message);
        }
        finally
        {
            ct.ThrowIfCancellationRequested();
        }
    }

    /// <summary>A measurement and, when it failed, why — so zeros are never mistaken for an answer.</summary>
    private sealed record Measured<T>(T Value, string? Error);
}
