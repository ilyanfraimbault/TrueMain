namespace Ingestor.Riot;

public interface IRiotHttpExecutor
{
    Task<T> GetAsync<T>(
        HttpClient httpClient,
        Uri uri,
        int maxRetryAttempts,
        string clientName,
        CancellationToken ct);
}
