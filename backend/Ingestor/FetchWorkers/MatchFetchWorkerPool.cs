using Core;
using Core.Lol.Identifiers;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Common;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Processes.Summaries;
using Ingestor.Riot.RateLimiting;
using Ingestor.Services;
using Microsoft.Extensions.Options;

namespace Ingestor.FetchWorkers;

/// <summary>
/// Runs match fetching as permanent workers, one per regional routing value (#1457).
/// </summary>
/// <remarks>
/// <para>
/// Riot's application limit is a <em>rate per routing host</em>, so every minute match
/// fetching spends waiting for the rest of a sequential lane is budget lost for good. Each
/// worker here claims a batch of its own platforms' accounts, fetches and writes it, and
/// claims again — the <see cref="MatchIngestionProcess"/> step of the fetch lane, unchanged,
/// narrowed through <see cref="MatchIngestionScope"/> and run in a loop instead of once a
/// pass. The rest of the lane runs beside it, on its own cadence, in the worker's
/// maintenance pass.
/// </para>
/// <para>
/// Throughput is set by <c>MatchFetchWorkers:TargetRequestsPerMinute</c>, enforced by the
/// rate limiter as a ceiling on the <c>MatchIngestion</c> caller, and the shares the limiter
/// reserves for the other regional callers keep the workers from starving them.
/// </para>
/// <para>
/// A worker owns its platforms and nothing else: it reaps only their expired claims, and
/// a batch a shutdown cancels releases its claims on the way out, so a stop strands nothing
/// even before the next boot's orphan release runs.
/// </para>
/// </remarks>
public sealed class MatchFetchWorkerPool(
    IServiceScopeFactory scopeFactory,
    IIterationContext iterationContext,
    ICallerContext callerContext,
    IRiotRateLimiter rateLimiter,
    IOptions<MatchIngestionOptions> matchOptions,
    IOptions<MatchFetchWorkersOptions> workerOptions,
    IngestorMetrics metrics,
    TimeProvider timeProvider,
    ILogger<MatchFetchWorkerPool> logger)
{
    /// <summary>
    /// How long the release of a cancelled batch's claims may take. Spent inside the
    /// container's stop grace period, like the cancellation record of a run.
    /// </summary>
    private static readonly TimeSpan ShutdownReleaseBudget = TimeSpan.FromSeconds(10);

    private const string MatchIngestionCaller = "MatchIngestion";

    /// <summary>
    /// The routing values the workers run on, each with the configured platforms it serves,
    /// in a stable order. A platform that does not parse has no route and is left out — the
    /// claim would never reach it anyway — with a warning naming it.
    /// </summary>
    public IReadOnlyList<MatchFetchRoute> Routes()
    {
        var routes = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var platform in PlatformNormalizer.Normalize(matchOptions.Value.Platforms))
        {
            if (!PlatformId.TryParse(platform, out var parsed))
            {
                logger.LogWarning("Platform {Platform} has no regional route; no fetch worker will claim it.", platform);
                continue;
            }

            var routingValue = parsed.Route.ToRegional().ToRegionalHost();
            if (!routes.TryGetValue(routingValue, out var platforms))
            {
                platforms = [];
                routes[routingValue] = platforms;
            }

            platforms.Add(platform);
        }

        return routes.Select(route => new MatchFetchRoute(route.Key, route.Value)).ToList();
    }

    /// <summary>
    /// Runs one worker per route until <paramref name="ct"/> is cancelled — or, when
    /// <paramref name="runOnce"/>, until each has run one batch, which is what <c>Job:RunOnce</c>
    /// means for this mode: one pass of everything the lane owns.
    /// </summary>
    public async Task RunAsync(bool runOnce, CancellationToken ct)
    {
        var routes = Routes();
        if (routes.Count == 0)
        {
            logger.LogWarning("No platform with a regional route is configured (MatchIngestion:Platforms); no fetch worker started.");
            return;
        }

        var options = workerOptions.Value;
        var batchSize = options.BatchSize
            ?? Math.Max(1, (int)Math.Ceiling(matchOptions.Value.BatchSize / (double)routes.Count));

        rateLimiter.SetCallerCeiling(MatchIngestionCaller, options.TargetRequestsPerMinute);
        logger.LogInformation(
            "Starting {WorkerCount} match-fetch worker(s) ({Routes}): {BatchSize} accounts per claim, at most {TargetRequestsPerMinute} Riot calls a minute per route.",
            routes.Count,
            string.Join(", ", routes.Select(route => $"{route.RoutingValue}={string.Join('+', route.Platforms)}")),
            batchSize,
            options.TargetRequestsPerMinute);

        try
        {
            await Task.WhenAll(routes.Select(route => RunRouteAsync(route, batchSize, runOnce, options, ct)));
        }
        finally
        {
            rateLimiter.SetCallerCeiling(MatchIngestionCaller, null);
        }
    }

    private async Task RunRouteAsync(
        MatchFetchRoute route,
        int batchSize,
        bool runOnce,
        MatchFetchWorkersOptions options,
        CancellationToken ct)
    {
        // Off the caller's flow: the routes must not run one after another until the first
        // await, and the AsyncLocal iteration and caller each batch opens must stay its own.
        await Task.Yield();

        while (!ct.IsCancellationRequested)
        {
            var outcome = await RunBatchAsync(route, batchSize, ct);
            if (runOnce)
            {
                return;
            }

            var pause = outcome switch
            {
                BatchOutcome.Empty => TimeSpan.FromSeconds(options.IdleDelaySeconds),
                BatchOutcome.Failed => TimeSpan.FromSeconds(options.FailureBackoffSeconds),
                _ => TimeSpan.Zero
            };

            if (pause > TimeSpan.Zero)
            {
                await Task.Delay(pause, timeProvider, ct);
            }
        }
    }

    private async Task<BatchOutcome> RunBatchAsync(MatchFetchRoute route, int batchSize, CancellationToken ct)
    {
        // A fresh scope per batch, for the reason the lane opens one per step: its own
        // DbContext, nothing cached from one batch into the next.
        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MatchIngestionScope>().Restrict(route.Platforms, batchSize);
        var process = scope.ServiceProvider.GetKeyedService<IIngestorProcess>(JobMode.MatchIngestionOnly)
            ?? throw new InvalidOperationException(
                $"No IIngestorProcess is registered for {nameof(JobMode)}.{JobMode.MatchIngestionOnly}, "
                + $"which the {route.RoutingValue} fetch worker runs.");

        // One iteration per batch, stamped with the mode the host runs: the admin reads a
        // batch as the worker half of the lane, beside its maintenance passes.
        using var iteration = iterationContext.BeginIteration(JobMode.FetchWorkers);
        using var caller = callerContext.BeginCall(process.Name);

        try
        {
            var summary = await process.RunCoreAsync(ct);
            // A batch whose every account failed backs off like a failed batch: the failures
            // were reverted to Queued, so claiming again at once would re-fail the same
            // accounts in a loop for as long as whatever broke them (Riot down, a database
            // hiccup) lasts.
            return summary switch
            {
                MatchIngestionSummary { AccountsProcessed: 0, Errors: 0 } => BatchOutcome.Empty,
                MatchIngestionSummary { AccountsProcessed: 0 } => BatchOutcome.Failed,
                MatchIngestionSummary => BatchOutcome.Worked,
                _ => BatchOutcome.Empty
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await ReleaseAbandonedClaimsAsync(route, scope.ServiceProvider);
            throw;
        }
        catch (Exception ex)
        {
            // A failed batch must not end the worker: the claims it reverted are claimable
            // again, and the next one starts after a backoff. The run is already recorded as
            // Failed by the decorator; this counts it, as the lane does for a failed step.
            logger.LogError(ex, "Match-fetch worker {RoutingValue} batch failed; retrying after a backoff.", route.RoutingValue);
            metrics.RecordRunFailure(process.Name, JobMode.FetchWorkers);
            return BatchOutcome.Failed;
        }
    }

    private async Task ReleaseAbandonedClaimsAsync(MatchFetchRoute route, IServiceProvider services)
    {
        // Detached and time-boxed: the batch's own token is cancelled by now, and a store that
        // does not answer must not hold the shutdown open. If this fails, the next boot's
        // orphan release frees the same rows.
        using var releaseCts = new CancellationTokenSource(ShutdownReleaseBudget, timeProvider);
        try
        {
            var released = await services.GetRequiredService<IMatchClaimService>()
                .ReleaseOrphanedClaimsAsync(route.Platforms, releaseCts.Token);
            if (!released.IsEmpty)
            {
                logger.LogInformation(
                    "Match-fetch worker {RoutingValue} released {Candidates} candidate(s) and {Accounts} account claim(s) on shutdown.",
                    route.RoutingValue,
                    released.Candidates,
                    released.Accounts);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Match-fetch worker {RoutingValue} could not release its claims on shutdown; the next boot will.",
                route.RoutingValue);
        }
    }

    private enum BatchOutcome
    {
        Worked,
        Empty,
        Failed
    }
}

/// <summary>A regional routing value and the configured platforms its fetch worker claims.</summary>
public sealed record MatchFetchRoute(string RoutingValue, IReadOnlyList<string> Platforms);
