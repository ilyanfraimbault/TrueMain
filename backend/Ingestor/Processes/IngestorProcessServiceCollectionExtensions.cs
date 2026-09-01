using Ingestor.Options;

namespace Ingestor.Processes;

public static class IngestorProcessServiceCollectionExtensions
{
    /// <summary>
    /// Registers every <see cref="IIngestorProcess"/> keyed by the
    /// <see cref="JobMode"/> it implements. Exactly one process must be
    /// registered per single-process mode: <see cref="JobMode.Full"/> is a
    /// composite handled by <see cref="JobModeSequence"/> and is deliberately
    /// left unkeyed.
    /// </summary>
    /// <remarks>
    /// Listed in pipeline order for readability only — the worker's execution
    /// order comes from <see cref="JobModeSequence"/>, not from this method.
    /// Always register through <see cref="RecordedProcessServiceCollectionExtensions.AddRecordedProcess{TProcess}"/>:
    /// the per-process catch in the worker assumes every production registration
    /// is wrapped in <see cref="RecordedProcess{TInner}"/> so a failure is still
    /// persisted as a Failed run. A process registered without the wrapper still
    /// runs and logs, but its runs are invisible to process health.
    /// </remarks>
    public static IServiceCollection AddIngestorProcesses(this IServiceCollection services)
    {
        services.AddRecordedProcess<LadderSyncProcess>(JobMode.LadderSyncOnly);
        services.AddRecordedProcess<DiscoveryProcess>(JobMode.DiscoveryOnly);
        services.AddRecordedProcess<ManualSeedProcess>(JobMode.ManualSeedOnly);
        services.AddRecordedProcess<HarvestProcess>(JobMode.HarvestOnly);
        services.AddRecordedProcess<ScoringProcess>(JobMode.ScoringOnly);
        services.AddRecordedProcess<MainActivityProcess>(JobMode.MainActivityOnly);
        services.AddRecordedProcess<MatchIngestionProcess>(JobMode.MatchIngestionOnly);
        services.AddRecordedProcess<MatchTeamPositionCorrectionProcess>(JobMode.TeamPositionCorrectionOnly);
        services.AddRecordedProcess<MainAnalysisProcess>(JobMode.MainAnalysisOnly);
        services.AddRecordedProcess<MatchParticipantEloBracketEnrichmentProcess>(JobMode.EloBracketEnrichmentOnly);
        services.AddRecordedProcess<RunePageDeduplicationProcess>(JobMode.RunePageDeduplicationOnly);
        services.AddRecordedProcess<ChampionPatternAggregationProcess>(JobMode.PatternAggregationOnly);
        services.AddRecordedProcess<ChampionMatchupLeadAggregationProcess>(JobMode.MatchupLeadAggregationOnly);
        services.AddRecordedProcess<ChampionLaneOutcomeAggregationProcess>(JobMode.LaneOutcomeAggregationOnly);
        services.AddRecordedProcess<ChampionSynergyAggregationProcess>(JobMode.SynergyAggregationOnly);
        services.AddRecordedProcess<ChampionBanAggregationProcess>(JobMode.BanAggregationOnly);
        services.AddRecordedProcess<ChampionPowerspikeAggregationProcess>(JobMode.PowerspikeAggregationOnly);
        services.AddRecordedProcess<AccountRefreshProcess>(JobMode.AccountRefreshOnly);
        services.AddRecordedProcess<MatchDataRetentionProcess>(JobMode.MatchDataRetentionOnly);
        services.AddRecordedProcess<StorageSnapshotProcess>(JobMode.StorageSnapshotOnly);
        return services;
    }
}
