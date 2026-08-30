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
    IJungleFirstClearRepository JungleFirstClears { get; }
    IMatchBanRepository MatchBans { get; }
    IRankSnapshotRepository RankSnapshots { get; }
    IDiscoveryCursorRepository DiscoveryCursors { get; }
    ILadderSyncCursorRepository LadderSyncCursors { get; }

    Task<int> SaveChangesAsync(CancellationToken ct);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
}
