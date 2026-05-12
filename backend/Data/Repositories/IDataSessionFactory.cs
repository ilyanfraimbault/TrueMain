namespace Data.Repositories;

public interface IDataSessionFactory
{
    Task<IDataSession> CreateAsync(CancellationToken ct);
}
