using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Health;
using TrueMain.Services.Personas;

namespace TrueMain.Services.Ops.Navigation;

public interface INavSummaryQueryService
{
    /// <summary>The sidebar's counts and health verdict (#2294).</summary>
    Task<NavSummaryReadModel> GetAsync(CancellationToken ct);
}

/// <summary>
/// The admin sidebar's badges (#2294). Every open admin tab asks once a minute, so both halves
/// of the read are kept light.
///
/// <para>
/// <b>Counts</b> come from two index-served queries — the conflicts' <c>(Status, LastSeenAtUtc)</c>
/// index, the personas' partial name-key index — cached <see cref="CountsTtl"/> and
/// single-flighted, so any number of tabs is one pair of queries per period.
/// </para>
///
/// <para>
/// <b>The verdict</b> is the warm pipeline-health payload's (#2305), peeked without counting as a
/// read: the sidebar is on every page, and were it a reader the background refresher would keep
/// evaluating the cockpit for as long as any admin tab is open. It is never computed here either:
/// with no evaluation held, or only one older than <see cref="HealthMaxAge"/>, the verdict is
/// <c>unknown</c>. The Overview and Health pages are the readers that keep it current.
/// </para>
/// </summary>
public sealed class NavSummaryQueryService(
    TrueMainDbContext db,
    IPersonaHomonymQueryService homonyms,
    IMemoryCache cache,
    TimeProvider? clock = null) : INavSummaryQueryService
{
    internal const string CountsCacheKey = "ops:nav-summary:counts";

    internal static readonly TimeSpan CountsTtl = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A verdict older than this is not shown: the refresher stops recomputing a payload nobody
    /// read for its idle window, so past it the value says what the pipeline was, not what it is.
    /// </summary>
    internal static readonly TimeSpan HealthMaxAge = CockpitCacheRefresher.IdleWindow;

    // Static so concurrent requests, each with its own scoped instance, share one pass.
    private static readonly RequestCoalescer<Counts> Coalescer = new();

    private TimeProvider Clock => clock ?? TimeProvider.System;

    public async Task<NavSummaryReadModel> GetAsync(CancellationToken ct)
    {
        var counts = await GetCountsAsync(ct);
        var health = CurrentHealth(CachedPipelineHealthQueryService.Payload.PeekValue(cache), Clock.GetUtcNow().UtcDateTime);

        return new NavSummaryReadModel
        {
            OpenConflicts = counts.OpenConflicts,
            HomonymGroups = counts.HomonymGroups,
            CountedAtUtc = counts.CountedAtUtc,
            HealthStatus = health?.Status ?? "unknown",
            HealthEvaluatedAtUtc = health?.EvaluatedAtUtc,
        };
    }

    /// <summary>The held evaluation while it is recent enough to stand for now, otherwise null.</summary>
    internal static PipelineHealthReadModel? CurrentHealth(PipelineHealthReadModel? held, DateTime nowUtc) =>
        held is not null && nowUtc - held.EvaluatedAtUtc <= HealthMaxAge ? held : null;

    private async Task<Counts> GetCountsAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CountsCacheKey, out Counts? cached) && cached is not null)
        {
            return cached;
        }

        return await Coalescer.GetOrJoinAsync(
            CountsCacheKey,
            async () =>
            {
                var openConflicts = await db.PersonaAccountConflicts
                    .AsNoTracking()
                    .LongCountAsync(c => c.Status == PersonaConflictStatus.Open, CancellationToken.None);
                var homonymGroups = await homonyms.CountPendingAsync(CancellationToken.None);
                return cache.Store(
                    CountsCacheKey,
                    new Counts(openConflicts, homonymGroups, Clock.GetUtcNow().UtcDateTime),
                    CountsTtl);
            },
            ct,
            // Both queries borrow the request-scoped DbContext (see RequestCoalescer).
            ownerAwaitsToCompletion: true);
    }

    private sealed record Counts(long OpenConflicts, long HomonymGroups, DateTime CountedAtUtc);
}
