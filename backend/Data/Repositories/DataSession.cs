using Microsoft.EntityFrameworkCore.Storage;

namespace Data.Repositories;

public sealed class DataSession : IDataSession
{
    private readonly TrueMainDbContext _db;

    public DataSession(TrueMainDbContext db)
    {
        _db = db;

        MainCandidates = new MainCandidateRepository(_db);
        MainChampionStats = new MainChampionStatRepository(_db);
        RiotAccounts = new RiotAccountRepository(_db);
        Matches = new MatchRepository(_db);
        MatchParticipants = new MatchParticipantRepository(_db);
        MatchParticipantTimelineSnapshots = new MatchParticipantTimelineSnapshotRepository(_db);
        MatchParticipantKillPositions = new MatchParticipantKillPositionRepository(_db);
        JungleFirstClears = new JungleFirstClearRepository(_db);
        ProcessRuns = new ProcessRunRepository(_db);
        RankSnapshots = new RankSnapshotRepository(_db);
        SeedRequests = new SeedRequestRepository(_db);
        DiscoveryCursors = new DiscoveryCursorRepository(_db);
    }

    public IMainCandidateRepository MainCandidates { get; }
    public IMainChampionStatRepository MainChampionStats { get; }
    public IRiotAccountRepository RiotAccounts { get; }
    public IMatchRepository Matches { get; }
    public IMatchParticipantRepository MatchParticipants { get; }
    public IMatchParticipantTimelineSnapshotRepository MatchParticipantTimelineSnapshots { get; }
    public IMatchParticipantKillPositionRepository MatchParticipantKillPositions { get; }
    public IJungleFirstClearRepository JungleFirstClears { get; }
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
