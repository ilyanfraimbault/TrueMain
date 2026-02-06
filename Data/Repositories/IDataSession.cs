using Microsoft.EntityFrameworkCore.Storage;

namespace Data.Repositories;

public interface IDataSession : IAsyncDisposable
{
    IMainCandidateRepository MainCandidates { get; }
    IMainChampionStatRepository MainChampionStats { get; }
    IRiotAccountRepository RiotAccounts { get; }
    IMatchRepository Matches { get; }
    IMatchParticipantRepository MatchParticipants { get; }

    Task<int> SaveChangesAsync(CancellationToken ct);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
}
