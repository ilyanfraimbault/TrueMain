using Core;
using Core.Lol.Identifiers;
using Data.Entities;
using Data.Logging.Mongo;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes.Components.Discovery;
using Ingestor.Riot;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

/// <summary>
/// Drains the "seed by Riot ID" intake (#409): claims <c>Pending</c>
/// <see cref="SeedRequest"/> rows the API recorded, resolves each Riot ID to a
/// PUUID via account-v1, upserts the <see cref="RiotAccount"/> and its
/// mastery-derived <see cref="MainCandidate"/>s (reusing the Discovery
/// components), then promotes those candidates straight to
/// <see cref="MainCandidateStatus.Queued"/> — skipping the competitive top-N
/// <c>ScoringProcess</c> so an explicitly-seeded account is always ingested. The
/// shared backbone for the admin "add a main" panel (#410) and bulk OTP import
/// (#411).
/// </summary>
public sealed class ManualSeedProcess(
    ILogger<ManualSeedProcess> logger,
    IRiotAccountClient riotAccountClient,
    IRiotPlatformClient riotPlatformClient,
    IDataSessionFactory sessionFactory,
    IAccountUpsertService accountUpsertService,
    ICandidateUpsertService candidateUpsertService,
    IAuditLog auditLog,
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

    public async Task<object?> RunCoreAsync(CancellationToken ct)
    {
        var options = manualSeedOptions.Value;
        var batchSize = Math.Max(1, options.BatchSize);

        List<Guid> pendingIds = await LoadPendingIdsAsync(batchSize, ct);
        if (pendingIds.Count == 0)
        {
            logger.LogInformation("No pending seed requests.");
            return new { reason = "No pending seed requests.", claimed = 0 };
        }

        var summary = new SeedSummary();
        foreach (var id in pendingIds)
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

        return new
        {
            claimed = summary.Claimed,
            ingested = summary.Ingested,
            notFound = summary.NotFound,
            failed = summary.Failed,
            candidatesQueued = summary.CandidatesQueued
        };
    }

    private async Task<List<Guid>> LoadPendingIdsAsync(int batchSize, CancellationToken ct)
    {
        await using var session = await sessionFactory.CreateAsync(ct);
        var pending = await session.SeedRequests.GetPendingAsync(batchSize, ct);
        return pending.Select(request => request.Id).ToList();
    }

    private async Task ProcessRequestAsync(Guid id, ManualSeedOptions options, SeedSummary summary, CancellationToken ct)
    {
        // A fresh session per request: a Riot/DB failure on one request must not
        // poison the change tracker for the rest of the batch, and each request's
        // claim + resolution + terminal state is its own unit of work.
        await using var session = await sessionFactory.CreateAsync(ct);

        // Atomic claim: flip Pending -> Resolving in a single UPDATE so two
        // concurrent runs can't both pick the same request (no read-then-write
        // TOCTOU window). A zero rowcount means another run already claimed it
        // (or the status changed / row vanished) between our batch scan and now.
        var claimed = await session.SeedRequests.ClaimAsync(id, ct);
        if (claimed == 0)
        {
            return;
        }

        summary.Claimed++;

        // Re-read the now-Resolving row tracked so the resolution path can
        // transition it to its terminal state and SaveChanges.
        var request = await session.SeedRequests.GetByIdAsync(id, ct);
        if (request is null)
        {
            return;
        }

        try
        {
            await ResolveAndIngestAsync(session, request, options, summary, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Interrupted (host shutdown / cancellation) after we claimed the
            // request as Resolving. Reset it to Pending so a later run can
            // re-claim it: GetPendingAsync only loads Pending rows and
            // SeedRequestService treats a lingering Resolving row as the
            // idempotent result, so leaving it Resolving would strand it forever.
            // Use CancellationToken.None — ct is already cancelled.
            await ResetToPendingAsync(request.Id, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            // Any Riot/DB failure terminates this request as Failed with a
            // (truncated) error, leaving the rest of the batch unaffected. Use a
            // detached save path so a corrupt tracked graph can't block recording
            // the failure.
            logger.LogWarning(ex, "Seed request {SeedRequestId} failed.", request.Id);
            summary.Failed++;
            await MarkFailedAsync(request.Id, ex.Message, ct);
        }
    }

    private async Task ResolveAndIngestAsync(
        IDataSession session,
        SeedRequest request,
        ManualSeedOptions options,
        SeedSummary summary,
        CancellationToken ct)
    {
        var platform = PlatformId.Parse(request.PlatformId).Route;
        var regional = platform.ToRegional();

        var account = await riotAccountClient.GetByRiotIdAsync(request.GameName, request.TagLine, regional, ct);
        if (account is null || string.IsNullOrWhiteSpace(account.Puuid))
        {
            request.Status = SeedRequestStatus.Failed;
            request.Error = "Riot ID not found";
            request.ProcessedAtUtc = DateTime.UtcNow;
            await session.SaveChangesAsync(ct);
            summary.NotFound++;
            return;
        }

        var nowUtc = DateTime.UtcNow;

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

        request.Status = SeedRequestStatus.Ingested;
        request.Error = null;
        request.ResolvedPuuid = account.Puuid;
        request.ResolvedRiotAccountId = upsert.Account.Id;
        request.ProcessedAtUtc = DateTime.UtcNow;
        await session.SaveChangesAsync(ct);
        summary.Ingested++;

        // Operator-action audit: the seed request has now resolved to a real
        // account and been queued for ingestion. Record the terminal outcome with
        // the resolved identity. Synchronous insert, never the diagnostic-log
        // channel.
        //
        // Best-effort by design, and ISOLATED from the processing-failure path: the
        // request is already committed as Ingested above. If this audit insert threw
        // and escaped, ProcessRequestAsync's catch would call MarkFailedAsync and
        // flip a SUCCESSFUL account to Failed (also double-counting it). So we catch
        // here and only log a Warning — under a Mongo outage the seed still succeeds
        // and only the audit event is missed. "Lossless" means the audit channel is
        // synchronous and unbatched vs the lossy batched diagnostic channel — it is
        // not a guarantee against a Mongo outage.
        try
        {
            await auditLog.RecordAsync(
                action: "seed_account_ingested",
                actor: "ingestor",
                targetType: nameof(SeedRequest),
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

    private async Task MarkFailedAsync(Guid id, string message, CancellationToken ct)
    {
        // Record the failure on its own session so a poisoned change tracker from
        // the failed attempt can't prevent the status write. Fall back to a no-op
        // if the row vanished.
        await using var session = await sessionFactory.CreateAsync(ct);
        var request = await session.SeedRequests.GetByIdAsync(id, ct);
        if (request is null)
        {
            return;
        }

        request.Status = SeedRequestStatus.Failed;
        request.Error = Truncate(message, MaxErrorLength);
        request.ProcessedAtUtc = DateTime.UtcNow;
        await session.SaveChangesAsync(ct);
    }

    private async Task ResetToPendingAsync(Guid id, CancellationToken ct)
    {
        // Own session: the interrupted attempt's session/change tracker may be in
        // an unusable state, and we must not depend on the (cancelled) ct.
        await using var session = await sessionFactory.CreateAsync(ct);
        await session.SeedRequests.ResetResolvingToPendingAsync(id, ct);
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
