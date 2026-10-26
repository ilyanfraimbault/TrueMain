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

    private static EffectiveConfigurationSectionDescriptor ChampionsList { get; } = new()
    {
        SectionName = ChampionsListOptions.SectionName,
        OptionsType = typeof(ChampionsListOptions),
        Title = "Champion thresholds",
        Description =
            "Every sample floor the public champion surfaces judge against: the games a "
            + "(champion, lane) line needs to be listed and ranked, the separate and higher "
            + "floor a build slice needs before its distribution is called a usable sample, "
            + "and the matchup/synergy floors. The two games floors are deliberately different "
            + "numbers, which is most of the reason this section is worth showing."
    };

    /// <summary>
    /// The pipeline-health alerter (#2292). Its include-list leaves the webhook URL out — it is
    /// the channel's credential — and shows <see cref="AlertingOptions.Enabled"/> in its place,
    /// so the page says whether alerting is on without saying where it posts.
    /// </summary>
    internal static EffectiveConfigurationSectionDescriptor Alerting { get; } = new()
    {
        SectionName = AlertingOptions.SectionName,
        OptionsType = typeof(AlertingOptions),
        Title = "Health alerting",
        Description =
            "When the /health verdict is announced on Discord: how often it is evaluated, how "
            + "many red evaluations make an incident, the per-signal cooldown and the failed-run "
            + "streak at which a process is announced.",
        IncludeProperties =
        [
            nameof(AlertingOptions.Enabled),
            nameof(AlertingOptions.AdminBaseUrl),
            nameof(AlertingOptions.EnvironmentLabel),
            nameof(AlertingOptions.EvaluationInterval),
            nameof(AlertingOptions.RedConfirmations),
            nameof(AlertingOptions.Cooldown),
            nameof(AlertingOptions.ProcessFailureThreshold)
        ],
        Notices =
        [
            new EffectiveConfigurationNotice(
                nameof(AlertingOptions.Enabled),
                UnsetCondition.ZeroOrNegative,
                "Alerting is off: no Discord webhook is configured. Set Alerting:DiscordWebhookUrl "
                + "(ALERTING_DISCORD_WEBHOOK_URL in the stack environment) to turn it on."),
            new EffectiveConfigurationNotice(
                nameof(AlertingOptions.AdminBaseUrl),
                UnsetCondition.EmptyText,
                "Alerts name the /health path instead of linking to it.")
        ]
    };

    public static EffectiveConfigurationCatalog Instance { get; } = new(
        ProcessName: "Api",
        Sections:
        [
            SharedEffectiveConfigurationSections.MainAnalysis,
            SharedEffectiveConfigurationSections.Database,
            SharedEffectiveConfigurationSections.MongoLogging,
            ChampionsList,
            DataQualityDetectors,
            StorageHistory,
            Alerting
        ]);
}
