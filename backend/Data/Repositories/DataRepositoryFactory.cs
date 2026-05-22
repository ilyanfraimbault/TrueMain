namespace Data.Repositories;

public sealed class DataRepositoryFactory : IDataRepositoryFactory
{
    public IMainCandidateRepository CreateMainCandidateRepository(TrueMainDbContext dbContext)
        => new MainCandidateRepository(dbContext);

    public IMainChampionStatRepository CreateMainChampionStatRepository(TrueMainDbContext dbContext)
        => new MainChampionStatRepository(dbContext);

    public IRiotAccountRepository CreateRiotAccountRepository(TrueMainDbContext dbContext)
        => new RiotAccountRepository(dbContext);

    public IMatchRepository CreateMatchRepository(TrueMainDbContext dbContext)
        => new MatchRepository(dbContext);

    public IMatchParticipantRepository CreateMatchParticipantRepository(TrueMainDbContext dbContext)
        => new MatchParticipantRepository(dbContext);

    public IProcessRunRepository CreateProcessRunRepository(TrueMainDbContext dbContext)
        => new ProcessRunRepository(dbContext);

    public IRankSnapshotRepository CreateRankSnapshotRepository(TrueMainDbContext dbContext)
        => new RankSnapshotRepository(dbContext);
}
