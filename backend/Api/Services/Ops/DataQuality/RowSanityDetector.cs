using System.Globalization;
using Core.Lol.Patches;
using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.DataQuality;

/// <summary>
/// The row-level sanity detector: predicate counts of arithmetically impossible or
/// zero-sample aggregate rows, plus per-patch match volumes against the median of the
/// comparable patches.
/// </summary>
internal sealed class RowSanityDetector(TrueMainDbContext db, IOptions<MainAnalysisOptions> mainAnalysisOptions)
{
    public async Task<DataQualityDetectorReadModel> BuildAsync(
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
        var sanityStatuses = rows.Select(DetectorCards.ParseStatus).ToList();
        var patchRows = await BuildPatchVolumeRowsAsync(settings, ct);
        rows.AddRange(patchRows);
        sanityStatuses.AddRange(patchRows
            .Select(DetectorCards.ParseStatus)
            .Where(status => status is not DetectorStatus.Unknown));

        var thinPatches = patchRows.Count(row => DetectorCards.ParseStatus(row) is DetectorStatus.Amber or DetectorStatus.Red);
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
                DetectorCards.Threshold("impossible rows", settings.InconsistentAggregateRowsAmber, settings.InconsistentAggregateRowsRed, "count"),
                DetectorCards.Threshold("zero-sample rows", settings.ZeroSampleAggregateRowsAmber, settings.ZeroSampleAggregateRowsRed, "count"),
                // A floor, not a ceiling: a patch is anomalous when its match count falls
                // *below* this share of the median patch.
                DetectorCards.Threshold("patch volume vs median", settings.PatchVolumeAnomalyRatio, 0, "ratio", "below")
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
}
