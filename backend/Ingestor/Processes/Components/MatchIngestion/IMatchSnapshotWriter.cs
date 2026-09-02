using Core.Lol.Identifiers;
using Data.Entities;
using Data.Repositories;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.MatchIngestion;

public interface IMatchSnapshotWriter
{
    /// <summary>
    /// Everything the account's snapshot ingestion needs before it writes anything:
    /// the match-v5 id list, the fresh matches' payloads and the reads that decide
    /// what to write. Riot calls and reads only — no write, so the caller runs this
    /// <em>outside</em> its transaction (#264, #1229).
    /// </summary>
    Task<SnapshotIngestionPlan> PrepareAsync(
        IDataSession session,
        string platformId,
        string puuid,
        RegionalRoute region,
        int matchesPerAccount,
        int maxFetchConcurrency,
        CancellationToken ct);

    /// <summary>
    /// Persists a plan produced by <see cref="PrepareAsync"/>. Writes only, so it is
    /// the part the caller wraps in a transaction.
    /// </summary>
    Task<SnapshotIngestionResult> WriteAsync(
        IDataSession session,
        SnapshotIngestionPlan plan,
        string platformId,
        string puuid,
        int saveBatchSize,
        CancellationToken ct);
}

/// <summary>
/// The materialised result of the fetch phase. <see cref="TargetMatches"/> is bounded
/// by <c>MatchIngestion:MatchesPerAccount</c> (20 at the shipped defaults), so holding
/// the payloads in memory between the two phases costs a few MB per account, not the
/// unbounded heap that OOM-killed the pattern aggregation (#600).
/// </summary>
/// <param name="AllMatchIds">Every id match-v5 returned for the account.</param>
/// <param name="ExistingMatchIds">The subset already persisted — skipped, but still backfilled.</param>
/// <param name="TargetMatches">Fresh matches in the tracked queue, with their payloads.</param>
/// <param name="ParticipantAccounts">Accounts referenced by those matches, resolved once for the batch.</param>
/// <param name="TrackedAccountId">The claimed account's row id, when it exists.</param>
/// <param name="SkippedWrongQueue">
/// Matches fetched and then discarded for being off-queue — calls that stored nothing. Carried
/// on the plan because the discard happens in the fetch phase but is only reported by the write
/// phase's result (#1358).
/// </param>
public sealed record SnapshotIngestionPlan(
    IReadOnlyList<string> AllMatchIds,
    IReadOnlyList<string> ExistingMatchIds,
    IReadOnlyList<FetchedMatch> TargetMatches,
    IReadOnlyDictionary<AccountKey, RiotAccount> ParticipantAccounts,
    Guid? TrackedAccountId,
    int SkippedWrongQueue);

public sealed record FetchedMatch(string MatchId, RiotMatchDto Dto);

/// <summary>
/// One account's snapshot pass. <see cref="Skipped"/> counts ids we already had stored — the
/// healthy kind of skip, one that costs no <c>/matches/{id}</c> call. <see cref="SkippedWrongQueue"/>
/// counts matches we paid to fetch and then discarded for being off-queue: those are the calls
/// that store nothing, and with <c>queue</c> now sent on the ids call (#1358) the counter should
/// sit at zero. It is split out rather than folded into <see cref="Skipped"/> precisely so a
/// non-zero value is visible instead of hidden inside a number that is normally large.
/// </summary>
public sealed record SnapshotIngestionResult(
    IReadOnlyCollection<string> AllMatchIds,
    IReadOnlyCollection<string> NewMatchIds,
    int Inserted,
    int Skipped,
    int SkippedWrongQueue);
