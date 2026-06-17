using Microsoft.EntityFrameworkCore.Storage;

namespace Data.Repositories;

public sealed class DataSession : IDataSession
{
    private readonly TrueMainDbContext _db;

    public DataSession(TrueMainDbContext db, IDataRepositoryFactory repositoryFactory)
    {
        _db = db;

        MainCandidates = repositoryFactory.CreateMainCandidateRepository(_db);
        MainChampionStats = repositoryFactory.CreateMainChampionStatRepository(_db);
        RiotAccounts = repositoryFactory.CreateRiotAccountRepository(_db);
        Matches = repositoryFactory.CreateMatchRepository(_db);
        MatchParticipants = repositoryFactory.CreateMatchParticipantRepository(_db);
        MatchParticipantTimelineSnapshots = repositoryFactory.CreateMatchParticipantTimelineSnapshotRepository(_db);
        ProcessRuns = repositoryFactory.CreateProcessRunRepository(_db);
        RankSnapshots = repositoryFactory.CreateRankSnapshotRepository(_db);
        SeedRequests = repositoryFactory.CreateSeedRequestRepository(_db);
        DiscoveryCursors = repositoryFactory.CreateDiscoveryCursorRepository(_db);
    }

    public IMainCandidateRepository MainCandidates { get; }
    public IMainChampionStatRepository MainChampionStats { get; }
    public IRiotAccountRepository RiotAccounts { get; }
    public IMatchRepository Matches { get; }
    public IMatchParticipantRepository MatchParticipants { get; }
    public IMatchParticipantTimelineSnapshotRepository MatchParticipantTimelineSnapshots { get; }
    public IProcessRunRepository ProcessRuns { get; }
    public IRankSnapshotRepository RankSnapshots { get; }
    public ISeedRequestRepository SeedRequests { get; }
    public IDiscoveryCursorRepository DiscoveryCursors { get; }

    public Task<int> SaveChangesAsync(CancellationToken ct)
        => _db.SaveChangesAsync(ct);

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct)
        => _db.Database.BeginTransactionAsync(ct);

    public ValueTask DisposeAsync()
        => _db.DisposeAsync();
}
