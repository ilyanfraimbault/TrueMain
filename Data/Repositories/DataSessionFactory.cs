using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class DataSessionFactory : IDataSessionFactory
{
    private readonly IDbContextFactory<TrueMainDbContext> _dbContextFactory;
    private readonly IDataRepositoryFactory _repositoryFactory;

    public DataSessionFactory(
        IDbContextFactory<TrueMainDbContext> dbContextFactory,
        IDataRepositoryFactory repositoryFactory)
    {
        _dbContextFactory = dbContextFactory;
        _repositoryFactory = repositoryFactory;
    }

    public async Task<IDataSession> CreateAsync(CancellationToken ct)
    {
        var db = await _dbContextFactory.CreateDbContextAsync(ct);
        return new DataSession(db, _repositoryFactory);
    }
}
