using Data.Configuration;
using TrueMain.Options;

namespace TrueMain.Configuration;

/// <summary>
/// The Api's half of the effective-configuration allow-list (#1034): the thresholds that
/// drive the data-quality panel and the storage-growth forecast, plus the sections shared
/// with the Ingestor.
///
/// <para>
/// Unlike the Ingestor's catalog, the Api never publishes this to Mongo: it can introspect
/// its own <c>IOptions</c> directly, so <c>EffectiveConfigurationQueryService</c> builds
/// this snapshot live on every request instead of reading a boot-time snapshot back.
/// </para>
/// </summary>
public static class ApiEffectiveConfigurationCatalog
{
    // Declared ahead of Instance: static members initialize in declaration order, and
    // Instance's collection expression below reads both of these.
    private static EffectiveConfigurationSectionDescriptor DataQualityDetectors { get; } = new()
    {
        SectionName = DataQualityDetectorOptions.SectionName,
        OptionsType = typeof(DataQualityDetectorOptions),
        Title = "Data-quality detectors",
        Description =
            "Every green/amber/red line drawn on the /data-quality panel: staleness, "
            + "orphan-ratio and queue-depth thresholds, grouped by the detector they gate."
    };

    private static EffectiveConfigurationSectionDescriptor StorageHistory { get; } = new()
    {
        SectionName = StorageHistoryOptions.SectionName,
        OptionsType = typeof(StorageHistoryOptions),
        Title = "Storage history",
        Description =
            "Knobs for the /database storage-growth panel: the volume's real capacity, the "
            + "fill levels the forecast projects a crossing date for, and how much history "
            + "it fits against.",
        Notices =
        [
            new EffectiveConfigurationNotice(
                nameof(StorageHistoryOptions.DiskCapacityBytes),
                UnsetCondition.ZeroOrNegative,
                "The /database disk-capacity forecast renders its absent state: growth and "
                + "the daily rate still chart, but no threshold-crossing date is projected (#925).")
        ]
    };

    public static EffectiveConfigurationCatalog Instance { get; } = new(
        ProcessName: "Api",
        Sections:
        [
            SharedEffectiveConfigurationSections.MainAnalysis,
            SharedEffectiveConfigurationSections.Database,
            SharedEffectiveConfigurationSections.MongoLogging,
            DataQualityDetectors,
            StorageHistory
        ]);
}
