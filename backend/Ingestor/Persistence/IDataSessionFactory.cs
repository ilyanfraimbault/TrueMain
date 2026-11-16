namespace Ingestor.Persistence;

public interface IDataSessionFactory
{
    Task<IDataSession> CreateAsync(CancellationToken ct);
}
