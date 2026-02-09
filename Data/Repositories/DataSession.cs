using Microsoft.EntityFrameworkCore.Storage;

namespace Data.Repositories;

public sealed class DataSession : IDataSession
{
    private readonly TrueMainDbContext _db;
    private readonly IDataRepositoryFactory _repositoryFactory;

    public DataSession(TrueMainDbContext db, IDataRepositoryFactory repositoryFactory)
    {
        _db = db;
        _repositoryFactory = repositoryFactory;

        MainCandidates = _repositoryFactory.CreateMainCandidateRepository(_db);
        MainChampionStats = _repositoryFactory.CreateMainChampionStatRepository(_db);
        RiotAccounts = _repositoryFactory.CreateRiotAccountRepository(_db);
        Matches = _repositoryFactory.CreateMatchRepository(_db);
        MatchParticipants = _repositoryFactory.CreateMatchParticipantRepository(_db);
        ProcessRuns = _repositoryFactory.CreateProcessRunRepository(_db);
    }

    public IMainCandidateRepository MainCandidates { get; }
    public IMainChampionStatRepository MainChampionStats { get; }
    public IRiotAccountRepository RiotAccounts { get; }
    public IMatchRepository Matches { get; }
    public IMatchParticipantRepository MatchParticipants { get; }
    public IProcessRunRepository ProcessRuns { get; }

    public Task<int> SaveChangesAsync(CancellationToken ct)
        => _db.SaveChangesAsync(ct);

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct)
        => _db.Database.BeginTransactionAsync(ct);

    public ValueTask DisposeAsync()
        => _db.DisposeAsync();
}
