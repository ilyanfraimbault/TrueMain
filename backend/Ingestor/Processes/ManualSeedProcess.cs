using Core;
using Core.Lol.Identifiers;
using Data.Entities;
using Data.Logging;
using Data.Logging.Mongo;
using Data.Ops.Mongo;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes.Components.Discovery;
using Ingestor.Processes.Summaries;
using Ingestor.Riot;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

/// <summary>
/// Drains the "seed by Riot ID" intake (#409): claims Pending
/// <see cref="SeedRequestDocument"/>s the API recorded, resolves each Riot ID to a
/// PUUID via account-v1, upserts the <see cref="RiotAccount"/> and its
/// mastery-derived <see cref="MainCandidate"/>s (reusing the Discovery
/// components), then promotes those candidates straight to
/// <see cref="MainCandidateStatus.Queued"/> — skipping the competitive top-N
/// <c>ScoringProcess</c> so an explicitly-seeded account is always ingested. The
/// shared backbone for the admin "add a main" panel (#410) and bulk OTP import
/// (#411). The queue lives in Mongo with the rest of the admin-portal data; the
/// account/candidate writes stay in SQL.
/// </summary>
public sealed class ManualSeedProcess(
    ILogger<ManualSeedProcess> logger,
    IRiotAccountClient riotAccountClient,
    IRiotPlatformClient riotPlatformClient,
    IDataSessionFactory sessionFactory,
    ISeedRequestStore seedRequestStore,
    IAccountUpsertService accountUpsertService,
    ICandidateUpsertService candidateUpsertService,
    IAuditLog auditLog,
    TimeProvider timeProvider,
    IOptions<ManualSeedOptions> manualSeedOptions) : IIngestorProcess
{
    private const int MaxErrorLength = 2048;

    // Candidate statuses a manual seed promotes to Queued. New = freshly
    // upserted; Scored = previously discovered but didn't make the competitive
    // top-N. Queued/Processing/Validated are already in/through the pipeline and
    // Rejected was an explicit not-a-main decision, so none are requeued here.
    private static readonly MainCandidateStatus[] RequeueableStatuses =
        [MainCandidateStatus.New, MainCandidateStatus.Scored];

    public string Name => "ManualSeed";

    public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
    {
        var options = manualSeedOptions.Value;
        var batchSize = Math.Max(1, options.BatchSize);

        var pending = await seedRequestStore.GetPendingAsync(batchSize, ct);
        if (pending.Count == 0)
        {
            logger.LogInformation("No pending seed requests.");
            return new ManualSeedNoWorkSummary("No pending seed requests.", 0);
        }

        var summary = new SeedSummary();
        foreach (var id in pending.Select(request => request.Id))
        {
            ct.ThrowIfCancellationRequested();
            await ProcessRequestAsync(id, options, summary, ct);
        }

        logger.LogInformation(
            "Manual seed summary: claimed={Claimed}, ingested={Ingested}, notFound={NotFound}, failed={Failed}, candidatesQueued={CandidatesQueued}.",
            summary.Claimed,
            summary.Ingested,
            summary.NotFound,
            summary.Failed,
            summary.CandidatesQueued);

        return new ManualSeedSummary(
            summary.Claimed,
            summary.Ingested,
            summary.NotFound,
            summary.Failed,
            summary.CandidatesQueued);
    }

    private async Task ProcessRequestAsync(Guid id, ManualSeedOptions options, SeedSummary summary, CancellationToken ct)
    {
        // Atomic claim: flip Pending -> Resolving in a single guarded update so two
        // concurrent runs can't both pick the same request (no read-then-write
        // TOCTOU window). False means another run already claimed it (or the
        // status changed / the document vanished) between our batch scan and now.
        var claimed = await seedRequestStore.ClaimAsync(id, ct);
        if (!claimed)
        {
            return;
        }

        summary.Claimed++;

        // Re-read the now-Resolving document so the resolution path works from its
        // submitted identity.
        var request = await seedRequestStore.GetByIdAsync(id, ct);
        if (request is null)
        {
            return;
        }

        try
        {
            await ResolveAndIngestAsync(request, options, summary, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Interrupted (host shutdown / cancellation) after we claimed the
            // request as Resolving. Reset it to Pending so a later run can
            // re-claim it: GetPendingAsync only loads Pending documents and
            // SeedRequestService treats a lingering Resolving one as the
            // idempotent result, so leaving it Resolving would strand it forever.
            // Use CancellationToken.None — ct is already cancelled.
            await seedRequestStore.ResetResolvingToPendingAsync(request.Id, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            // Any Riot/DB failure terminates this request as Failed with a
            // (truncated) error, leaving the rest of the batch unaffected. Named
            // ops event (#444) so the terminal failure is filterable on /ops/logs.
            logger.LogWarning(OpsEvents.SeedRequestFailed, ex, "Seed request {SeedRequestId} failed.", request.Id);
            summary.Failed++;
            await seedRequestStore.MarkFailedAsync(
                request.Id, Truncate(ex.Message, MaxErrorLength), timeProvider.GetUtcNow().UtcDateTime, ct);
        }
    }

    private async Task ResolveAndIngestAsync(
        SeedRequestDocument request,
        ManualSeedOptions options,
        SeedSummary summary,
        CancellationToken ct)
    {
        var platform = PlatformId.Parse(request.PlatformId).Route;
        var regional = platform.ToRegional();

        var account = await riotAccountClient.GetByRiotIdAsync(request.GameName, request.TagLine, regional, ct);
        if (account is null || string.IsNullOrWhiteSpace(account.Puuid))
        {
            await seedRequestStore.MarkFailedAsync(request.Id, "Riot ID not found", timeProvider.GetUtcNow().UtcDateTime, ct);
            summary.NotFound++;

            // Named ops event (#444): an unresolvable Riot ID is the other
            // terminal failure of a seed request (typo, renamed account).
            logger.LogWarning(
                OpsEvents.SeedRequestFailed,
                "Seed request {SeedRequestId} failed: Riot ID {GameName}#{TagLine} not found.",
                request.Id,
                request.GameName,
                request.TagLine);
            return;
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        // A session per request: the account/candidate writes stay in SQL, and a
        // DB failure on one request must not poison the change tracker for the
        // rest of the batch.
        await using var session = await sessionFactory.CreateAsync(ct);

        // summoner-v4 gives the profile fields AccountUpsertService writes
        // (summonerId, icon, level); account-v1 above gives the authoritative
        // Riot ID identity, which Discovery's upsert intentionally leaves blank
        // (see #182) — so we backfill GameName/TagLine here from account-v1.
        var summoner = await riotPlatformClient.GetSummonerByPuuidAsync(platform, account.Puuid, ct);
        var upsert = await accountUpsertService.UpsertAsync(session, platform, summoner, nowUtc, ct);
        upsert.Account.GameName = account.GameName ?? string.Empty;
        upsert.Account.TagLine = account.TagLine;

        // Build the mastery-derived candidates exactly like Discovery, reusing
        // its component. CandidateUpsertService reads only TopChampionsPerAccount
        // and MaxLastPlayDays off DiscoveryOptions, so a thin adapter suffices.
        var masteries = await riotPlatformClient.GetChampionMasteriesAsync(platform, account.Puuid, ct);
        await candidateUpsertService.UpsertAsync(
            session,
            request.PlatformId,
            account.Puuid,
            masteries,
            new DiscoveryOptions
            {
                TopChampionsPerAccount = options.TopChampionsPerAccount,
                MaxLastPlayDays = options.MaxLastPlayDays
            },
            nowUtc,
            ct);

        // Persist the account + freshly-added (New) candidates before promoting:
        // SetStatusForAccountAsync runs as a set-based ExecuteUpdate against the
        // DB, so the rows must exist first.
        await session.SaveChangesAsync(ct);

        // Promote this account's candidates straight to Queued, skipping the
        // competitive top-N ScoringProcess — an explicitly-seeded account is
        // always meant to be ingested. Requeue from BOTH New (freshly upserted)
        // and Scored: re-seeding a previously-discovered account whose candidates
        // already lost competitive scoring must still ingest it, otherwise
        // MatchIngestionProcess (which only picks Queued candidates) would never
        // touch it and this request would report success without ingesting.
        var queued = await session.MainCandidates.SetStatusForAccountAsync(
            request.PlatformId,
            account.Puuid,
            RequeueableStatuses,
            MainCandidateStatus.Queued,
            ct);
        summary.CandidatesQueued += queued;

        await seedRequestStore.MarkIngestedAsync(
            request.Id, account.Puuid, upsert.Account.Id, timeProvider.GetUtcNow().UtcDateTime, ct);
        summary.Ingested++;

        // Named ops event (#444): the seed request reached its successful terminal
        // state. Information-level, persisted by the Mongo sink via the OpsEvents
        // bypass and filterable on /ops/logs (the audit_events record below stays
        // the lossless operator-action trail).
        logger.LogInformation(
            OpsEvents.SeedRequestResolved,
            "Seed request {SeedRequestId} resolved: {GameName}#{TagLine} on {Platform} ingested, {CandidatesQueued} candidate(s) queued.",
            request.Id,
            request.GameName,
            request.TagLine,
            request.PlatformId,
            queued);

        // Operator-action audit: the seed request has now resolved to a real
        // account and been queued for ingestion. Record the terminal outcome with
        // the resolved identity. Synchronous insert, never the diagnostic-log
        // channel.
        //
        // Best-effort by design, and ISOLATED from the processing-failure path: the
        // request is already marked Ingested above. If this audit insert threw
        // and escaped, ProcessRequestAsync's catch would call MarkFailedAsync and
        // flip a SUCCESSFUL account to Failed (also double-counting it). So we catch
        // here and only log a Warning — under a Mongo outage the seed still succeeds
        // and only the audit event is missed. "Lossless" here means the audit channel
        // is synchronous and unbatched vs the lossy batched diagnostic channel — it
        // is not a guarantee against a Mongo outage.
        try
        {
            await auditLog.RecordAsync(
                action: "seed_account_ingested",
                actor: "ingestor",
                targetType: "SeedRequest",
                targetId: request.Id.ToString(),
                metadata: new Dictionary<string, string>
                {
                    ["gameName"] = request.GameName,
                    ["tagLine"] = request.TagLine,
                    ["platformId"] = request.PlatformId,
                    ["resolvedPuuid"] = account.Puuid,
                    ["candidatesQueued"] = queued.ToString()
                },
                ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Audit write failed for ingested seed request {SeedRequestId}; the account was ingested, audit event missed.",
                request.Id);
        }
    }

    private static string? Truncate(string? value, int maxLength)
        => string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];

    private sealed class SeedSummary
    {
        public int Claimed { get; set; }
        public int Ingested { get; set; }
        public int NotFound { get; set; }
        public int Failed { get; set; }
        public int CandidatesQueued { get; set; }
    }
}
