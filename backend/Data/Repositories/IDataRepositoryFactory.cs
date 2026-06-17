namespace Data.Repositories;

public interface IDataRepositoryFactory
{
    IMainCandidateRepository CreateMainCandidateRepository(TrueMainDbContext dbContext);
    IMainChampionStatRepository CreateMainChampionStatRepository(TrueMainDbContext dbContext);
    IRiotAccountRepository CreateRiotAccountRepository(TrueMainDbContext dbContext);
    IMatchRepository CreateMatchRepository(TrueMainDbContext dbContext);
    IMatchParticipantRepository CreateMatchParticipantRepository(TrueMainDbContext dbContext);
    IMatchParticipantTimelineSnapshotRepository CreateMatchParticipantTimelineSnapshotRepository(TrueMainDbContext dbContext);
    IProcessRunRepository CreateProcessRunRepository(TrueMainDbContext dbContext);
    IRankSnapshotRepository CreateRankSnapshotRepository(TrueMainDbContext dbContext);
    ISeedRequestRepository CreateSeedRequestRepository(TrueMainDbContext dbContext);
    IDiscoveryCursorRepository CreateDiscoveryCursorRepository(TrueMainDbContext dbContext);
}
