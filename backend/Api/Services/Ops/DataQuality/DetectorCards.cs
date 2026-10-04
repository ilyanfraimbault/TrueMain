using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.DataQuality;

/// <summary>
/// The shaping the detector cards share: thresholds, row-status parsing and the
/// tracked-platform reads that keep a silent platform from reading as a healthy one.
/// </summary>
internal static class DetectorCards
{
    /// <summary>
    /// Platforms we track accounts on. Read from <c>riot_accounts</c> (a few thousand
    /// rows) rather than from a DISTINCT over <c>matches</c>, which would be the scan
    /// these detectors exist to avoid.
    /// </summary>
    public static async Task<IReadOnlyList<string>> LoadTrackedPlatformsAsync(TrueMainDbContext db, CancellationToken ct)
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
    public static IEnumerable<DataQualityDetectorRowReadModel> MissingPlatformRows(
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

    public static DataQualityThresholdReadModel Threshold(
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

    public static DetectorStatus ParseStatus(DataQualityDetectorRowReadModel row) => row.Status switch
    {
        "green" => DetectorStatus.Green,
        "amber" => DetectorStatus.Amber,
        "red" => DetectorStatus.Red,
        _ => DetectorStatus.Unknown
    };
}
