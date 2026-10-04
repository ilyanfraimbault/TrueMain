using System.Globalization;
using Core.Lol.Patches;
using TrueMain.Options;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.DataQuality;

namespace TrueMain.Services.Ops.Coverage;

/// <summary>
/// The assembly half of the patch-coverage view (#1033): turns the measured counts and
/// the <see cref="PatchCoverageEvaluator"/> verdicts into the read-model rows and the
/// sentences an operator reads, with no database and no clock of its own.
/// </summary>
internal static class PatchCoverageRows
{
    public const string SourceNote =
        "Ingestion is grouped over `matches` (and its join to `match_participants`) for the covered patches; "
        + "coverage groups `champion_aggregate_scopes` on the same (champion, lane) grain, queue filter and games "
        + "floor the champion directory reads with; each fold is one grouped rollup of its own table. None of those "
        + "tables is indexed on its patch column, so this is a set of grouped scans — affordable behind an explicit "
        + "navigation, which is why it is its own page rather than a card on the overview.";

    public static PatchCoverageRowReadModel BuildPatchRow(
        string patch,
        bool isCurrent,
        PatchIngestion? ingestion,
        PatchCoverage? coverage,
        IReadOnlyList<FoldMeasurement> folds,
        PatchCoverageBar bar,
        int floor,
        DataQualityDetectorOptions detectorSettings,
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
            Folds = [.. folds.Select(fold => BuildFoldRow(fold, patch, ingestion, detectorSettings, now))]
        };
    }

    private static PatchFoldCoverageReadModel BuildFoldRow(
        FoldMeasurement fold,
        string patch,
        PatchIngestion? ingestion,
        DataQualityDetectorOptions settings,
        DateTime now)
    {
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

    public static string BuildHeadline(PatchCoverageRowReadModel? current, PatchCoverageRowReadModel newestIngested)
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

    public static string FloorNote(int floor)
        => floor <= 0
            ? "No games floor is configured (ChampionsList:MinSampleGames is 0), so every line with a single game is served."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"A (champion, lane) line needs at least {floor} games on a patch before the champion directory lists it and the tier list ranks it — ChampionsList:MinSampleGames. Lines below it are dropped from the payload entirely, so a thin patch reads as a short list rather than as an error.");
}
