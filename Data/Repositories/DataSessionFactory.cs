using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class DataSessionFactory : IDataSessionFactory
{
    private readonly IDbContextFactory<TrueMainDbContext> _dbContextFactory;

    public DataSessionFactory(IDbContextFactory<TrueMainDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<IDataSession> CreateAsync(CancellationToken ct)
    {
        var db = await _dbContextFactory.CreateDbContextAsync(ct);
        return new DataSession(db);
    }
}
