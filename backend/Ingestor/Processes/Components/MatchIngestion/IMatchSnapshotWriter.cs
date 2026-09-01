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
public sealed record SnapshotIngestionPlan(
    IReadOnlyList<string> AllMatchIds,
    IReadOnlyList<string> ExistingMatchIds,
    IReadOnlyList<FetchedMatch> TargetMatches,
    IReadOnlyDictionary<AccountKey, RiotAccount> ParticipantAccounts,
    Guid? TrackedAccountId);

public sealed record FetchedMatch(string MatchId, RiotMatchDto Dto);

public sealed record SnapshotIngestionResult(
    IReadOnlyCollection<string> AllMatchIds,
    IReadOnlyCollection<string> NewMatchIds,
    int Inserted,
    int Skipped);
