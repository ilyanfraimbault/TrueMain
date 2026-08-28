using Microsoft.EntityFrameworkCore.Storage;

namespace Data.Repositories;

public interface IDataSession : IAsyncDisposable
{
    IMainCandidateRepository MainCandidates { get; }
    IMainChampionStatRepository MainChampionStats { get; }
    IRiotAccountRepository RiotAccounts { get; }
    IMatchRepository Matches { get; }
    IMatchParticipantRepository MatchParticipants { get; }
    IMatchParticipantTimelineSnapshotRepository MatchParticipantTimelineSnapshots { get; }
    IMatchParticipantKillPositionRepository MatchParticipantKillPositions { get; }
    IMatchBanRepository MatchBans { get; }
    IRankSnapshotRepository RankSnapshots { get; }
    IDiscoveryCursorRepository DiscoveryCursors { get; }

    Task<int> SaveChangesAsync(CancellationToken ct);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);

    /// <summary>
    /// Detaches everything the change tracker holds, so a long batched loop stops
    /// re-running <c>DetectChanges</c> over every entity it has ever touched — a cost
    /// that grows quadratically with the number of batches (#1229).
    /// </summary>
    /// <remarks>
    /// Only call it right after a <see cref="SaveChangesAsync"/>, and only when nothing
    /// loaded before the call is mutated after it: a detached entity accepts property
    /// writes and persists none of them, so a stale reference turns into silent data
    /// loss rather than an error. Callers that preload entities for a whole run must
    /// move that preload inside the batch before clearing.
    /// </remarks>
    void ClearTracking();
}
