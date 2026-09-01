using System.Globalization;
using Core.Lol.Patches;
using Core.Options;
using Data;
using Data.Aggregation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// Answers "is the current patch servable?" (#1033).
///
/// <para>
/// <b>Why the numbers here must mirror the public read exactly.</b> The coverage figure
/// groups <c>champion_aggregate_scopes</c> by <c>(champion, lane)</c> on the configured
/// queue, drops lane-less rows and compares the summed games against
/// <c>ChampionsList:MinSampleGames</c> — the same grain, the same filter and the same
/// floor <see cref="Champions.ChampionSummariesQueryService"/> applies. Anything else
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
                FloorNote = FloorNote(floor),
                Verdict = "unknown",
                Status = DetectorStatus.Unknown.ToWireName(),
                Headline = "No match and no aggregate row carries a usable patch, so there is nothing to judge.",
                SourceNote = SourceNote,
                EvaluatedAtUtc = now
            };
        }

        var patchArray = coveredPatches.ToArray();

        var ingestion = await SafeAsync("ingestion", () => LoadIngestionAsync(queueId, patchArray, ct), ct);
        var coverage = await SafeAsync(
            "coverage",
            () => LoadCoverageAsync(queueId, coveredPatches, scopeVersionsByPatch, floor, settings, ct),
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
                FloorNote = FloorNote(floor),
                CurrentPatch = currentPatch,
                Verdict = "unknown",
                Status = DetectorStatus.Unknown.ToWireName(),
                Headline = "Patch coverage could not be measured, so no patch has a verdict.",
                UnknownReason = ingestion.Error ?? coverage.Error,
                SourceNote = SourceNote,
                EvaluatedAtUtc = now
            };
        }

        var folds = await LoadFoldsAsync(coverage.Value, ct);

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
            .Select(patch => BuildPatchRow(
                patch,
                patch == currentPatch,
                ingestion.Value.GetValueOrDefault(patch),
                coverage.Value.GetValueOrDefault(patch),
                folds,
                bar,
                floor,
                now))
            .ToList();

        var current = rows.FirstOrDefault(row => row.IsCurrent);
        var newestIngested = rows[0];

        return new PatchCoverageReadModel
        {
            QueueId = queueId,
            MinSampleGames = floor,
            FloorNote = FloorNote(floor),
            CurrentPatch = currentPatch,
            Verdict = current?.Verdict ?? "unknown",
            Status = current?.Status ?? DetectorStatus.Unknown.ToWireName(),
            Headline = BuildHeadline(current, newestIngested),
            Patches = rows,
            SourceNote = SourceNote,
            EvaluatedAtUtc = now
        };
    }

    // ---- verdict -------------------------------------------------------------

    private PatchCoverageRowReadModel BuildPatchRow(
        string patch,
        bool isCurrent,
        PatchIngestion? ingestion,
        PatchCoverage? coverage,
        IReadOnlyList<FoldMeasurement> folds,
        PatchCoverageBar bar,
        int floor,
        DateTime now)
    {
        var matches = ingestion?.Matches ?? 0;
        var lines = coverage?.Lines ?? 0;
        var linesPastFloor = coverage?.LinesPastFloor ?? 0;
        // Every scope row on the patch, lane-less sentinels included. "Has the fold run"
        // and "is the patch rankable" are different questions and need different counts.
        var aggregateRows = coverage?.BuildRows ?? 0;

        var verdict = PatchCoverageEvaluator.ReadVerdict(
            matches, aggregateRows, lines, linesPastFloor, bar.Value, isCurrent);

        // Worded from the verdict, never computed from the count alone: a sentence that
        // says "142 lines clear the floor" beside an amber badge leaves the reader to
        // guess which of the two low-coverage causes they are looking at.
        var headline = verdict.Verdict switch
        {
            "unknown" => "No match and no aggregate row on this patch — nothing to judge.",
            "notAggregated" => string.Create(
                CultureInfo.InvariantCulture,
                $"{matches} match(es) ingested and not one aggregate row yet — the folds have not reached this patch. Not thin: unaggregated."),
            "servable" => string.Create(
                CultureInfo.InvariantCulture,
                $"{linesPastFloor} of {lines} (champion, lane) lines clear the {floor}-game floor, at or above the bar of {bar.Value:F0} — enough for the directory and tier list to rank on."),
            // Aggregated, and still with nothing to rank. Worth its own sentence: the
            // generic thin wording would print "0 of 0 lines", which reads as a bug.
            "thin" when lines <= 0 => string.Create(
                CultureInfo.InvariantCulture,
                $"{aggregateRows} aggregate row(s) on this patch and not one carries a lane, so the directory and tier list have nothing to rank. Aggregated, not rankable."),
            _ => string.Create(
                CultureInfo.InvariantCulture,
                $"Only {linesPastFloor} of {lines} (champion, lane) lines clear the {floor}-game floor, against a bar of {bar.Value:F0}{(isCurrent ? " — and this is the patch the site serves, so the tier list is ranking on those lines" : string.Empty)}.")
        };

        return new PatchCoverageRowReadModel
        {
            Patch = patch,
            IsCurrent = isCurrent,
            Verdict = verdict.Verdict,
            Status = verdict.Status.ToWireName(),
            Headline = headline,
            Matches = matches,
            Participants = ingestion?.Participants ?? 0,
            FirstGameStartUtc = ingestion?.FirstGameStartUtc,
            LastGameStartUtc = ingestion?.LastGameStartUtc,
            Daily = ingestion?.Daily ?? [],
            Lines = lines,
            LinesPastFloor = linesPastFloor,
            Champions = coverage?.Champions ?? 0,
            ChampionsPastFloor = coverage?.ChampionsPastFloor ?? 0,
            ServableLinesBar = verdict.Judged ? bar.Value : null,
            ServableLinesBarNote = verdict.Judged ? bar.Note : null,
            BelowFloorCount = coverage?.BelowFloorCount ?? 0,
            BelowFloor = coverage?.BelowFloor ?? [],
            Folds = [.. folds.Select(fold => BuildFoldRow(fold, patch, ingestion, now))]
        };
    }

    private PatchFoldCoverageReadModel BuildFoldRow(
        FoldMeasurement fold,
        string patch,
        PatchIngestion? ingestion,
        DateTime now)
    {
        var settings = detectorOptions.Value;
        var pending = fold.Spec.Pending?.Invoke(ingestion);

        if (fold.UnknownReason is not null)
        {
            return new PatchFoldCoverageReadModel
            {
                Key = fold.Spec.Key,
                Label = fold.Spec.Label,
                Status = DetectorStatus.Unknown.ToWireName(),
                PendingMatches = pending,
                Note = "This fold could not be measured: " + fold.UnknownReason
            };
        }

        // A fold that shipped mid-corpus has no rows before it existed and never will:
        // raw match payloads are not kept, so there is nothing to backfill from. Reporting
        // that as 0 would read as "the fold is broken on this patch", which is the one
        // thing it is not.
        if (fold.FirstMeasuredPatch is { } first
            && PatchVersion.TryParse(patch, out var parsed)
            && PatchVersion.TryParse(first, out var parsedFirst)
            && parsed < parsedFirst)
        {
            return new PatchFoldCoverageReadModel
            {
                Key = fold.Spec.Key,
                Label = fold.Spec.Label,
                Measured = false,
                FirstMeasuredPatch = first,
                NotMeasuredNote = $"Not measured before {first} — the fold shipped mid-corpus and raw matches are not kept, so this patch can never be backfilled.",
                Status = DetectorStatus.Unknown.ToWireName(),
                PendingMatches = pending,
                Note = fold.Spec.Note
            };
        }

        var row = fold.ByPatch.GetValueOrDefault(patch);
        var age = DataQualityDetectorEvaluator.AgeHours(row?.LastAggregatedAtUtc, now);

        return new PatchFoldCoverageReadModel
        {
            Key = fold.Spec.Key,
            Label = fold.Spec.Label,
            FirstMeasuredPatch = fold.FirstMeasuredPatch,
            Rows = row?.Rows ?? 0,
            Champions = row?.Champions ?? 0,
            LastAggregatedAtUtc = row?.LastAggregatedAtUtc,
            AgeHours = age,
            Status = DataQualityDetectorEvaluator
                .Classify(age, settings.AggregationStaleAmberHours, settings.AggregationStaleRedHours)
                .ToWireName(),
            PendingMatches = pending,
            Note = fold.Spec.Note
        };
    }

    private static string BuildHeadline(PatchCoverageRowReadModel? current, PatchCoverageRowReadModel newestIngested)
    {
        if (current is null)
        {
            return "Nothing has been aggregated on any covered patch, so no patch is servable.";
        }

        // The newest ingested patch and the patch the site serves are different things,
        // and the gap between them is invisible on every public page.
        var waiting = !newestIngested.IsCurrent && newestIngested.Verdict == "notAggregated"
            ? string.Create(
                CultureInfo.InvariantCulture,
                $" Patch {newestIngested.Patch} has {newestIngested.Matches} ingested match(es) and no aggregate row, so the site is still serving {current.Patch}.")
            : string.Empty;

        return current.Headline + waiting;
    }

    private static string FloorNote(int floor)
        => floor <= 0
            ? "No games floor is configured (ChampionsList:MinSampleGames is 0), so every line with a single game is served."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"A (champion, lane) line needs at least {floor} games on a patch before the champion directory lists it and the tier list ranks it — ChampionsList:MinSampleGames. Lines below it are dropped from the payload entirely, so a thin patch reads as a short list rather than as an error.");

    private const string SourceNote =
        "Ingestion is grouped over `matches` (and its join to `match_participants`) for the covered patches; "
        + "coverage groups `champion_aggregate_scopes` on the same (champion, lane) grain, queue filter and games "
        + "floor the champion directory reads with; each fold is one grouped rollup of its own table. None of those "
        + "tables is indexed on its patch column, so this is a set of grouped scans — affordable behind an explicit "
        + "navigation, which is why it is its own page rather than a card on the overview.";

    // ---- measurement ---------------------------------------------------------

    private async Task<IReadOnlyDictionary<string, PatchIngestion>> LoadIngestionAsync(
        int queueId,
        string[] patches,
        CancellationToken ct)
    {
        // Normalising in SQL rather than reading every row: split_part matches
        // PatchVersion.Normalize for anything with two or more segments, and the
        // `= ANY(patches)` filter only ever admits values that already parsed, so a
        // degenerate GameVersion is excluded from both sides consistently.
        FormattableString dailySql = $"""
            SELECT
                split_part(m."GameVersion", '.', 1) || '.' || split_part(m."GameVersion", '.', 2) AS "Patch",
                to_char((m."GameStartTimeUtc" AT TIME ZONE 'UTC')::date, 'YYYY-MM-DD') AS "Date",
                count(*) AS "Matches",
                min(m."GameStartTimeUtc") AS "FirstGameStartUtc",
                max(m."GameStartTimeUtc") AS "LastGameStartUtc",
                count(*) FILTER (WHERE NOT m."TimelineIngested") AS "PendingTimeline",
                count(*) FILTER (WHERE m."TimelineIngested" AND NOT m."PowerspikeAggregated") AS "PendingPowerspike",
                count(*) FILTER (WHERE NOT m."SynergyAggregated") AS "PendingSynergy",
                count(*) FILTER (WHERE NOT m."MatchupLeadAggregated") AS "PendingMatchupLead",
                count(*) FILTER (WHERE NOT m."BansAggregated") AS "PendingBans",
                count(*) FILTER (WHERE NOT m."LaneOutcomeAggregated") AS "PendingLaneOutcome"
            FROM matches m
            WHERE m."QueueId" = {queueId}
              AND split_part(m."GameVersion", '.', 1) || '.' || split_part(m."GameVersion", '.', 2) = ANY({patches})
            GROUP BY 1, 2
            """;

        var daily = await db.Database.SqlQuery<PatchDaySqlRow>(dailySql).ToListAsync(ct);

        // Participants ride a second statement rather than a join in the one above: a
        // join multiplies the match rows ten-fold, and `count(DISTINCT m."Id")` over that
        // product costs far more than scanning `matches` twice.
        FormattableString participantsSql = $"""
            SELECT
                split_part(m."GameVersion", '.', 1) || '.' || split_part(m."GameVersion", '.', 2) AS "Patch",
                to_char((m."GameStartTimeUtc" AT TIME ZONE 'UTC')::date, 'YYYY-MM-DD') AS "Date",
                count(*) AS "Participants"
            FROM matches m
            JOIN match_participants p ON p."MatchId" = m."Id"
            WHERE m."QueueId" = {queueId}
              AND split_part(m."GameVersion", '.', 1) || '.' || split_part(m."GameVersion", '.', 2) = ANY({patches})
            GROUP BY 1, 2
            """;

        var participants = (await db.Database.SqlQuery<PatchDayParticipantsSqlRow>(participantsSql).ToListAsync(ct))
            .ToDictionary(row => (row.Patch, row.Date), row => row.Participants);

        return daily
            .GroupBy(row => row.Patch, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new PatchIngestion(
                    group.Sum(row => row.Matches),
                    group.Sum(row => participants.GetValueOrDefault((row.Patch, row.Date))),
                    group.Min(row => row.FirstGameStartUtc),
                    group.Max(row => row.LastGameStartUtc),
                    group.Sum(row => row.PendingTimeline),
                    group.Sum(row => row.PendingPowerspike),
                    group.Sum(row => row.PendingSynergy),
                    group.Sum(row => row.PendingMatchupLead),
                    group.Sum(row => row.PendingBans),
                    group.Sum(row => row.PendingLaneOutcome),
                    [.. group
                        .OrderBy(row => row.Date, StringComparer.Ordinal)
                        .Select(row => new PatchCoverageDayReadModel
                        {
                            Date = row.Date,
                            Matches = row.Matches,
                            Participants = participants.GetValueOrDefault((row.Patch, row.Date))
                        })]),
                StringComparer.Ordinal);
    }

    private async Task<IReadOnlyDictionary<string, PatchCoverage>> LoadCoverageAsync(
        int queueId,
        IReadOnlyList<string> coveredPatches,
        IReadOnlyDictionary<string, List<string>> scopeVersionsByPatch,
        int floor,
        PatchCoverageOptions settings,
        CancellationToken ct)
    {
        var versions = coveredPatches
            .SelectMany(patch => scopeVersionsByPatch.GetValueOrDefault(patch) ?? [])
            .ToList();

        if (versions.Count == 0)
        {
            return new Dictionary<string, PatchCoverage>(StringComparer.Ordinal);
        }

        // Exactly the grouping ChampionSummariesQueryService runs for the public
        // directory: same queue filter, same (champion, lane) key, same summed games
        // — and, since #1346, the same mains-only population. The two must agree:
        // this panel exists to tell an operator what the directory will show.
        var groups = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.QueueId == queueId && versions.Contains(scope.GameVersion))
            .Where(scope => scope.IsMain)
            .GroupBy(scope => new { scope.GameVersion, scope.ChampionId, scope.Position })
            .Select(group => new
            {
                group.Key.GameVersion,
                group.Key.ChampionId,
                group.Key.Position,
                Games = group.Sum(scope => scope.Games),
                Rows = group.LongCount(),
                LastAggregatedAtUtc = group.Max(scope => scope.AggregatedAtUtc)
            })
            .ToListAsync(ct);

        var limit = Math.Max(1, settings.ThinLineLimit);

        return groups
            // Collapse the raw GameVersion forms that normalise onto the same patch before
            // anything is counted: two forms of one patch are one patch, not two.
            .GroupBy(row => PatchVersion.Normalize(row.GameVersion), StringComparer.Ordinal)
            .ToDictionary(
                patchGroup => patchGroup.Key,
                patchGroup =>
                {
                    var buildRows = patchGroup.Sum(row => row.Rows);
                    var buildChampions = patchGroup.Select(row => row.ChampionId).Distinct().Count();
                    var buildLast = patchGroup.Max(row => row.LastAggregatedAtUtc);

                    // Folded through the shared definition, not a local copy of it: the
                    // count this page reports and the count the servable bar gates
                    // serving on (#1109) have to be the same number, or the page
                    // certifies a patch the site refused — or worse, blesses one it
                    // switched onto. Lane-less rows are dropped inside Fold.
                    var lines = ChampionDirectoryLines.Fold(patchGroup.Select(row =>
                        new ChampionDirectoryLine(patchGroup.Key, row.ChampionId, row.Position, row.Games)));

                    var pastFloor = lines.Where(line => ChampionDirectoryLines.ClearsFloor(line, floor)).ToList();
                    var below = lines.Where(line => !ChampionDirectoryLines.ClearsFloor(line, floor)).ToList();

                    return new PatchCoverage(
                        lines.Count,
                        pastFloor.Count,
                        lines.Select(line => line.ChampionId).Distinct().Count(),
                        pastFloor.Select(line => line.ChampionId).Distinct().Count(),
                        below.Count,
                        [.. below
                            // Closest to the floor first: a thin patch's real question is
                            // "how far off is it", and the lines about to clear answer it.
                            .OrderByDescending(line => line.Games)
                            .ThenBy(line => line.ChampionId)
                            .ThenBy(line => line.Position, StringComparer.Ordinal)
                            .Take(limit)
                            .Select(line => new PatchThinLineReadModel
                            {
                                ChampionId = line.ChampionId,
                                Position = line.Position,
                                Games = line.Games,
                                GamesToFloor = floor - line.Games
                            })],
                        buildRows,
                        buildChampions,
                        buildLast);
                },
                StringComparer.Ordinal);
    }

    /// <summary>
    /// One grouped rollup per fold table, over every patch rather than only the covered
    /// ones — the same scan then yields both the per-patch numbers and the oldest patch
    /// the fold has ever written, which is what turns a zero into "not measured before".
    /// </summary>
    private async Task<IReadOnlyList<FoldMeasurement>> LoadFoldsAsync(
        IReadOnlyDictionary<string, PatchCoverage> coverage,
        CancellationToken ct)
    {
        var measurements = new List<FoldMeasurement>
        {
            // Builds ride the coverage rollup that has already been read: it is the same
            // table on the same filter, so re-scanning it would buy nothing.
            BuildsFold(coverage)
        };

        foreach (var fold in DerivedFolds)
        {
            measurements.Add(await MeasureFoldAsync(fold, ct));
        }

        return measurements;
    }

    private static FoldMeasurement BuildsFold(IReadOnlyDictionary<string, PatchCoverage> coverage)
    {
        var spec = new FoldSpec(
            "builds",
            "Builds — champion_aggregate_scopes",
            "The table every patch-scoped public read rests on: the directory, the tier list and the build tabs. "
                + "Replace-by-scope per account, so it carries no per-match backlog.",
            Pending: null);

        var byPatch = coverage.ToDictionary(
            entry => entry.Key,
            entry => new FoldPatchRow(entry.Value.BuildRows, entry.Value.BuildChampions, entry.Value.BuildLastAggregatedAtUtc),
            StringComparer.Ordinal);

        // No first-measured cutoff: scopes have existed for the whole corpus, so an empty
        // patch here means the fold has not run, not that the patch is out of scope.
        return new FoldMeasurement(spec, byPatch, FirstMeasuredPatch: null, UnknownReason: null);
    }

    private async Task<FoldMeasurement> MeasureFoldAsync(DerivedFold fold, CancellationToken ct)
    {
        var spec = fold.Spec;

        try
        {
            var rows = await db.Database.SqlQueryRaw<FoldPatchSqlRow>(fold.Sql).ToListAsync(ct);

            var byPatch = rows
                .Where(row => PatchVersion.TryParse(row.Patch, out _))
                .GroupBy(row => PatchVersion.Normalize(row.Patch), StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => new FoldPatchRow(
                        group.Sum(row => row.Rows),
                        group.Sum(row => row.Champions),
                        group.Max(row => row.LastAggregatedAtUtc)),
                    StringComparer.Ordinal);

            // Only patches the fold actually produced something on count as "measured":
            // a row group with zero rows is what the FILTER variants (lane outcomes,
            // per-opponent spikes) return for a patch the fold predates.
            var firstMeasured = byPatch
                .Where(entry => entry.Value.Rows > 0)
                .Select(entry => entry.Key)
                .OrderBy(PatchVersion.Parse)
                .FirstOrDefault();

            return new FoldMeasurement(spec, byPatch, firstMeasured, UnknownReason: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Same rule as the detector panel: one fold's broken or unaffordable rollup
            // must not blind the other six, and must not read as an empty fold either.
            logger.LogWarning(ex, "Patch-coverage fold {Fold} failed to measure", spec.Key);
            return new FoldMeasurement(spec, new Dictionary<string, FoldPatchRow>(StringComparer.Ordinal), null, ex.Message);
        }
        finally
        {
            ct.ThrowIfCancellationRequested();
        }
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

    // ---- fold catalogue ------------------------------------------------------

    /// <summary>
    /// The folds read from their own table. Each is one grouped rollup keyed on the
    /// already-normalised <c>Patch</c> column; the FILTER variants split a second fold out
    /// of the same scan rather than paying for another one.
    /// </summary>
    private static readonly DerivedFold[] DerivedFolds =
    [
        new(
            new FoldSpec(
                "matchups",
                "Matchups — champion_matchup_stats",
                "Champion vs lane opponent win rates. Additive, so a thin patch fills in as matches fold.",
                ingestion => ingestion?.PendingMatchupLead),
            """
            SELECT
                "Patch" AS "Patch",
                count(*) AS "Rows",
                count(DISTINCT "ChampionId") AS "Champions",
                max("AggregatedAtUtc") AS "LastAggregatedAtUtc"
            FROM champion_matchup_stats
            GROUP BY "Patch"
            """),
        new(
            new FoldSpec(
                "laneOutcomes",
                "Lane outcomes — champion_matchup_stats (LaneGames)",
                "The 15-minute lane verdict folded onto the matchup rows (#919). Needs both lane participants to have "
                    + "a timeline snapshot, so it necessarily trails the matchup counts above.",
                ingestion => ingestion?.PendingLaneOutcome),
            """
            SELECT
                "Patch" AS "Patch",
                count(*) FILTER (WHERE "LaneGames" > 0) AS "Rows",
                count(DISTINCT "ChampionId") FILTER (WHERE "LaneGames" > 0) AS "Champions",
                max("AggregatedAtUtc") FILTER (WHERE "LaneGames" > 0) AS "LastAggregatedAtUtc"
            FROM champion_matchup_stats
            GROUP BY "Patch"
            """),
        new(
            new FoldSpec(
                "bans",
                "Bans — champion_ban_stats",
                "Ban counts per patch and elo band (#920). One-shot: raw match payloads are not kept, so the matches "
                    + "that predate the fold were flagged as already folded and can never contribute.",
                ingestion => ingestion?.PendingBans),
            """
            SELECT
                "Patch" AS "Patch",
                count(*) AS "Rows",
                count(DISTINCT "ChampionId") AS "Champions",
                max("AggregatedAtUtc") AS "LastAggregatedAtUtc"
            FROM champion_ban_stats
            GROUP BY "Patch"
            """),
        new(
            new FoldSpec(
                "powerspikes",
                "Power curves — champion_powerspike_curve_stats",
                "Per-minute gold/damage leads. Folded from timeline snapshots, so it trails timeline ingestion.",
                ingestion => ingestion?.PendingPowerspike),
            """
            SELECT
                "Patch" AS "Patch",
                count(*) AS "Rows",
                count(DISTINCT "ChampionId") AS "Champions",
                max("AggregatedAtUtc") AS "LastAggregatedAtUtc"
            FROM champion_powerspike_curve_stats
            GROUP BY "Patch"
            """),
        new(
            new FoldSpec(
                "powerspikeOpponents",
                "Power spikes by opponent — champion_powerspike_event_stats",
                "Spike rows carrying the lane opponent they were measured against (#957). Rows folded before it, and "
                    + "rows retention has collapsed, are rolled back to opponent 0 and are excluded here — which is "
                    + "why this count starts empty on a patch and fills in rather than arriving whole.",
                ingestion => ingestion?.PendingPowerspike),
            """
            SELECT
                "Patch" AS "Patch",
                count(*) FILTER (WHERE "OpponentChampionId" <> 0) AS "Rows",
                count(DISTINCT "ChampionId") FILTER (WHERE "OpponentChampionId" <> 0) AS "Champions",
                max("AggregatedAtUtc") FILTER (WHERE "OpponentChampionId" <> 0) AS "LastAggregatedAtUtc"
            FROM champion_powerspike_event_stats
            GROUP BY "Patch"
            """),
        new(
            new FoldSpec(
                "synergies",
                "Synergies — champion_synergy_stats",
                "Per-pairing win rates. Read behind the highest floor on the site (ChampionsList:MinSynergyGames), so "
                    + "it clears later than the directory does.",
                ingestion => ingestion?.PendingSynergy),
            """
            SELECT
                "Patch" AS "Patch",
                count(*) AS "Rows",
                count(DISTINCT "ChampionId") AS "Champions",
                max("AggregatedAtUtc") AS "LastAggregatedAtUtc"
            FROM champion_synergy_stats
            GROUP BY "Patch"
            """)
    ];

    // ---- internal shapes -----------------------------------------------------

    /// <summary>A measurement and, when it failed, why — so zeros are never mistaken for an answer.</summary>
    private sealed record Measured<T>(T Value, string? Error);

    private sealed record FoldSpec(
        string Key,
        string Label,
        string Note,
        Func<PatchIngestion?, long?>? Pending);

    /// <summary>A fold measured by its own grouped rollup, as opposed to one riding a scan already paid for.</summary>
    private sealed record DerivedFold(FoldSpec Spec, string Sql);

    private sealed record FoldMeasurement(
        FoldSpec Spec,
        IReadOnlyDictionary<string, FoldPatchRow> ByPatch,
        string? FirstMeasuredPatch,
        string? UnknownReason);

    private sealed record FoldPatchRow(long Rows, long Champions, DateTime? LastAggregatedAtUtc);

    private sealed record PatchIngestion(
        long Matches,
        long Participants,
        DateTime? FirstGameStartUtc,
        DateTime? LastGameStartUtc,
        long PendingTimeline,
        long PendingPowerspike,
        long PendingSynergy,
        long PendingMatchupLead,
        long PendingBans,
        long PendingLaneOutcome,
        IReadOnlyList<PatchCoverageDayReadModel> Daily);

    private sealed record PatchCoverage(
        long Lines,
        long LinesPastFloor,
        long Champions,
        long ChampionsPastFloor,
        long BelowFloorCount,
        IReadOnlyList<PatchThinLineReadModel> BelowFloor,
        long BuildRows,
        long BuildChampions,
        DateTime? BuildLastAggregatedAtUtc);

    private sealed record PatchDaySqlRow(
        string Patch,
        string Date,
        long Matches,
        DateTime? FirstGameStartUtc,
        DateTime? LastGameStartUtc,
        long PendingTimeline,
        long PendingPowerspike,
        long PendingSynergy,
        long PendingMatchupLead,
        long PendingBans,
        long PendingLaneOutcome);

    private sealed record PatchDayParticipantsSqlRow(string Patch, string Date, long Participants);

    private sealed record FoldPatchSqlRow(string Patch, long Rows, long Champions, DateTime? LastAggregatedAtUtc);
}
