using Data.Logging;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes.Common;
using Ingestor.Processes.Components.Coverage;
using Ingestor.Processes.Components.Discovery;
using Ingestor.Processes.Components.Intake;
using Ingestor.Processes.Summaries;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

/// <summary>
/// Participant-sourced candidate generator (#485): aggregates orphan
/// <c>match_participants</c> rows into <see cref="Data.Entities.MainCandidate"/>s
/// at near-zero Riot API cost (no Riot calls at all). Runs after Discovery /
/// ManualSeed and before Scoring, so harvested candidates compete in the same
/// per-platform top-N and flow through the same MatchIngestion -> MainAnalysis pass.
/// </summary>
public sealed class HarvestProcess(
    ILogger<HarvestProcess> logger,
    IDataSessionFactory sessionFactory,
    IParticipantHarvestService harvestService,
    IChampionCoverageProvider coverageProvider,
    TimeProvider timeProvider,
    IOptions<HarvestOptions> harvestOptions,
    IOptions<MatchIngestionOptions> matchIngestionOptions,
    IOptions<IntakeOptions> intakeOptions) : IIngestorProcess
{
    public string Name => "Harvest";

    public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
    {
        var configured = harvestOptions.Value;
        var options = ApplyIntakeBudget(configured);

        // Guard only: the repository normalizes Harvest:Platforms (trim/upper/distinct)
        // before the SQL filter, so it is the single source of truth — no need to rebuild
        // options with a pre-normalized list here.
        if (PlatformNormalizer.Normalize(options.Platforms).Count == 0)
        {
            logger.LogWarning("No platforms configured (Harvest:Platforms).");
            return new HarvestNoWorkSummary("No platforms configured.", 0);
        }

        await using var session = await sessionFactory.CreateAsync(ct);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        // The same frozen coverage signal the claim and scoring read, so the harvest's
        // per-platform budget split agrees with theirs within a cycle (#1150).
        var championCoverage = await coverageProvider.GetSnapshotAsync(session, ct);
        if (options.MaxCandidatesPerRun != configured.MaxCandidatesPerRun)
        {
            logger.LogInformation(
                "Harvest budget sized to the claim (#1361): configured {Configured} -> effective {Effective} "
                + "(new pairs {NewPairs} unchanged, refresh {Refresh} = claim capacity x Intake:PromotionHeadroomFactor), "
                + "newCandidateShare {ConfiguredShare} -> {EffectiveShare:0.###}.",
                configured.MaxCandidatesPerRun,
                options.MaxCandidatesPerRun,
                NewPairBudget(configured),
                options.MaxCandidatesPerRun - NewPairBudget(configured),
                configured.NewCandidateShare,
                options.NewCandidateShare);
        }
        var result = await harvestService.HarvestAsync(session, options, championCoverage, nowUtc, ct);

        // Named ops event (#444): one per harvest run, so the operator can follow the
        // participant-harvest arm from /ops/logs alongside ladder discovery. Coverage
        // (#495) rides on it: how many (puuid, champion) pairs qualified versus how many
        // the budget could take, split between new discovery and stat refresh.
        var coverage = result.Coverage;
        logger.LogInformation(
            OpsEvents.HarvestCycleCompleted,
            "Harvest summary: lookbackDays={LookbackDays}, maxCandidatesPerRun={MaxCandidatesPerRun}, newCandidateShare={NewCandidateShare}, minObservedGames={MinObservedGames}, candidatesInserted={Inserted}, candidatesUpdated={Updated}, accountsCreated={AccountsCreated}, eligibleNew={EligibleNew}, selectedNew={SelectedNew}, eligibleKnown={EligibleKnown}, selectedKnown={SelectedKnown}.",
            options.LookbackDays,
            options.MaxCandidatesPerRun,
            options.NewCandidateShare,
            options.MinObservedGames,
            result.CandidatesInserted,
            result.CandidatesUpdated,
            result.AccountsCreated,
            coverage.EligibleNew,
            coverage.SelectedNew,
            coverage.EligibleKnown,
            coverage.SelectedKnown);

        // The cap is a real bound on coverage, so it is never applied silently (#495): every
        // truncated run says so at Warning, with the per-platform split that also exposes an
        // imbalanced run (one region eating the cross-platform budget). droppedNew > 0 is the
        // one to act on — new discovery is being deferred, which is how the harvest starves.
        if (coverage.IsBudgetBound)
        {
            logger.LogWarning(
                OpsEvents.HarvestBudgetExhausted,
                "Harvest budget exhausted: maxCandidatesPerRun={MaxCandidatesPerRun} did not cover the eligible pool — droppedNew={DroppedNew} of {EligibleNew}, droppedKnown={DroppedKnown} of {EligibleKnown}. Per platform: {PerPlatform}.",
                options.MaxCandidatesPerRun,
                coverage.DroppedNew,
                coverage.EligibleNew,
                coverage.DroppedKnown,
                coverage.EligibleKnown,
                FormatPerPlatform(coverage));
        }

        return new HarvestSummary(
            result.CandidatesInserted,
            result.CandidatesUpdated,
            result.AccountsCreated,
            coverage.EligibleNew,
            coverage.SelectedNew,
            coverage.EligibleKnown,
            coverage.SelectedKnown,
            coverage.IsBudgetBound);
    }

    /// <summary>
    /// Re-sizes the run's budget so the refresh half is bounded by what the claim can absorb
    /// (#1361), leaving the discovery half exactly as configured.
    ///
    /// <para>
    /// The harvest exhausted its flat 7 500-row budget on every production run, ~7 450 of
    /// those rows being refreshes: re-reading observed stats for candidates that were already
    /// promoted, to recompute scores the claim would not reach for years. Discovery — finding
    /// (puuid, champion) pairs we have never seen — has no such dependency and keeps its full
    /// share, so this shrinks the write volume without shrinking the pool.
    /// </para>
    ///
    /// <para>
    /// Expressed as a smaller total plus a larger <c>NewCandidateShare</c> rather than as a
    /// second budget knob, because the share is what
    /// <c>ParticipantHarvestService.SelectWithinBudget</c> already reserves with — and its
    /// floor-not-partition semantics are preserved: a run whose new pairs run out still spends
    /// the remainder on refreshes.
    /// </para>
    /// </summary>
    private HarvestOptions ApplyIntakeBudget(HarvestOptions configured)
    {
        var newPairs = NewPairBudget(configured);
        var refresh = Math.Min(
            Math.Max(0, configured.MaxCandidatesPerRun - newPairs),
            IntakeCapacity.RefreshBudgetPerRun(matchIngestionOptions.Value, intakeOptions.Value));

        var effectiveBudget = Math.Max(1, newPairs + refresh);
        if (effectiveBudget >= configured.MaxCandidatesPerRun)
        {
            return configured;
        }

        return new HarvestOptions
        {
            Platforms = configured.Platforms,
            QueueId = configured.QueueId,
            MinObservedGames = configured.MinObservedGames,
            MaxCandidatesPerRun = effectiveBudget,
            NewCandidateShare = Math.Clamp(newPairs / (double)effectiveBudget, 0d, 1d),
            LookbackDays = configured.LookbackDays,
            SaveBatchSize = configured.SaveBatchSize
        };
    }

    /// <summary>
    /// The discovery half of the configured budget, kept intact by <see cref="ApplyIntakeBudget"/>.
    /// </summary>
    private static int NewPairBudget(HarvestOptions configured)
        => (int)Math.Ceiling(Math.Max(0, configured.MaxCandidatesPerRun) * Math.Clamp(configured.NewCandidateShare, 0d, 1d));

    private static string FormatPerPlatform(HarvestCoverage coverage)
        => string.Join(
            ", ",
            coverage.Platforms.Select(platform =>
                $"{platform.PlatformId} new={platform.SelectedNew}/{platform.EligibleNew} known={platform.SelectedKnown}/{platform.EligibleKnown}"));
}
