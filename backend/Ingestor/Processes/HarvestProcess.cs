using Data.Logging;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes.Common;
using Ingestor.Processes.Components.Discovery;
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
    IOptions<HarvestOptions> harvestOptions) : IIngestorProcess
{
    public string Name => "Harvest";

    public async Task<object?> RunCoreAsync(CancellationToken ct)
    {
        var options = harvestOptions.Value;

        // Guard only: the repository normalizes Harvest:Platforms (trim/upper/distinct)
        // before the SQL filter, so it is the single source of truth — no need to rebuild
        // options with a pre-normalized list here.
        if (PlatformNormalizer.Normalize(options.Platforms).Count == 0)
        {
            logger.LogWarning("No platforms configured (Harvest:Platforms).");
            return new { reason = "No platforms configured.", candidatesInserted = 0 };
        }

        await using var session = await sessionFactory.CreateAsync(ct);
        var nowUtc = DateTime.UtcNow;

        var result = await harvestService.HarvestAsync(session, options, nowUtc, ct);

        // Named ops event (#444): one per harvest run, so the operator can follow the
        // participant-harvest arm from /ops/logs alongside ladder discovery.
        logger.LogInformation(
            OpsEvents.HarvestCycleCompleted,
            "Harvest summary: minObservedGames={MinObservedGames}, candidatesInserted={Inserted}, candidatesUpdated={Updated}, accountsCreated={AccountsCreated}.",
            options.MinObservedGames,
            result.CandidatesInserted,
            result.CandidatesUpdated,
            result.AccountsCreated);

        return new
        {
            candidatesInserted = result.CandidatesInserted,
            candidatesUpdated = result.CandidatesUpdated,
            accountsCreated = result.AccountsCreated
        };
    }
}
