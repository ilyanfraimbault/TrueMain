using System.Linq.Expressions;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

/// <summary>
/// The two classes the match-ingest claim spends its batch on (#900), and the candidate side
/// of the breadth class. One place so the claim, the promotion ranking and the settle pass
/// agree on what "a new candidate" is.
/// <para>
/// <b>Depth</b> — accounts with an active established main, ordered by
/// <c>LastMatchIngestAtUtc</c>. <b>Breadth</b> — accounts never ingested that hold a
/// <c>Queued</c> candidate. An account we have already ingested is depth's to revisit when it
/// is due, never breadth's (#1535): its leftover <c>Scored</c> rows used to be promoted and
/// claimed again through breadth, spending a slot meant for an unseen player.
/// </para>
/// </summary>
public static class MatchIngestClaimClasses
{
    /// <summary>The depth arm: an active established main. Inactive mains are out (#900).</summary>
    public static Expression<Func<RiotAccount, bool>> EstablishedMain(TrueMainDbContext db)
        => account => db.MainChampionStats.Any(stat =>
            stat.IsMain
            && stat.IsActive
            && stat.PlatformId == account.PlatformId
            && stat.Puuid == account.Puuid);

    /// <summary>The breadth arm: never ingested, with a <c>Queued</c> candidate (#1535).</summary>
    public static Expression<Func<RiotAccount, bool>> NewCandidate(TrueMainDbContext db)
        => account => account.LastMatchIngestAtUtc == null
                      && db.MainCandidates.Any(candidate =>
                          candidate.Status == MainCandidateStatus.Queued
                          && candidate.PlatformId == account.PlatformId
                          && candidate.Puuid == account.Puuid);

    /// <summary>
    /// A candidate whose account has already been ingested. Such a row can no longer reach
    /// breadth, so it neither competes for promotion nor holds a place in the queue (#1535);
    /// it stays as the record that the pair was seen (#900, never deleted). An account row that
    /// does not exist yet is never-ingested by definition.
    /// </summary>
    public static Expression<Func<MainCandidate, bool>> AccountAlreadyIngested(TrueMainDbContext db)
        => candidate => db.RiotAccounts.Any(account =>
            account.PlatformId == candidate.PlatformId
            && account.Puuid == candidate.Puuid
            && account.LastMatchIngestAtUtc != null);

    /// <summary>The platform's promotable candidates, best first — see <c>GetScoredByPlatformAsync</c>.</summary>
    public static IQueryable<MainCandidate> PromotionRanking(
        TrueMainDbContext db,
        string platformId,
        int take,
        IReadOnlyCollection<int> deprioritizedChampionIds)
        => db.MainCandidates
            .Where(c => c.PlatformId == platformId && c.Status == MainCandidateStatus.Scored)
            .Where(Not(AccountAlreadyIngested(db)))
            // Champions that already reached the coverage target sort last whatever their
            // score (#900), so they only take the slots an under-covered champion left free.
            .OrderBy(c => deprioritizedChampionIds.Contains(c.ChampionId) ? 1 : 0)
            .ThenByDescending(c => c.Score)
            .ThenBy(c => c.ScoredAtUtc == null ? 0 : 1)
            .ThenBy(c => c.ScoredAtUtc)
            .ThenBy(c => c.Id)
            .Take(Math.Max(0, take));

    /// <summary>Ids of up to <paramref name="take"/> <c>Queued</c> rows whose account is already ingested.</summary>
    public static IQueryable<Guid> QueuedForIngestedAccounts(TrueMainDbContext db, string platformId, int take)
        => db.MainCandidates
            .AsNoTracking()
            .Where(c => c.PlatformId == platformId && c.Status == MainCandidateStatus.Queued)
            .Where(AccountAlreadyIngested(db))
            .OrderBy(c => c.Id)
            .Select(c => c.Id)
            .Take(Math.Max(1, take));

    private static Expression<Func<T, bool>> Not<T>(Expression<Func<T, bool>> predicate)
        => Expression.Lambda<Func<T, bool>>(Expression.Not(predicate.Body), predicate.Parameters);
}
