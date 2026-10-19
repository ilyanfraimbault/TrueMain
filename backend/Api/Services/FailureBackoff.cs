using Microsoft.Extensions.Caching.Memory;

namespace TrueMain.Services;

/// <summary>
/// A short negative cache in front of an expensive cached read (#2132). The response caches
/// only store successes, so a read that fails — typically a cold pass cancelled by the API's
/// 60 s <c>statement_timeout</c> (57014) — used to be re-run by the very next request, and an
/// admin page refreshing every 30 s kept a scan running against Postgres for as long as it
/// stayed open. After a failure, the read is not attempted again for the backoff window:
/// callers get a <see cref="ReadBackoffException"/> at once instead of another identical
/// timeout.
/// </summary>
/// <remarks>
/// Cancellation is not a failure of the read and records nothing. The marker goes through
/// <see cref="ApiCache.Store{T}"/> like any entry, so it carries a size under the shared
/// cache's <c>SizeLimit</c>.
/// </remarks>
internal static class FailureBackoff
{
    /// <summary>
    /// Runs <paramref name="compute"/> unless it failed under <paramref name="key"/> within the
    /// last <paramref name="backoff"/>, in which case it throws a
    /// <see cref="ReadBackoffException"/> carrying that failure without running it.
    /// </summary>
    public static async Task<T> RunAsync<T>(
        IMemoryCache cache,
        string key,
        TimeSpan backoff,
        Func<Task<T>> compute)
    {
        var failureKey = $"{key}:failed";
        if (cache.TryGetValue(failureKey, out Exception? lastFailure) && lastFailure is not null)
        {
            throw new ReadBackoffException(
                $"The read '{key}' failed less than {backoff.TotalSeconds:0} s ago and is not retried before then.",
                lastFailure);
        }

        try
        {
            return await compute();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            cache.Store(failureKey, ex, backoff);
            throw;
        }
    }
}

/// <summary>
/// Thrown instead of re-running a read that failed within its backoff window; the failure
/// that opened the window is the inner exception.
/// </summary>
public sealed class ReadBackoffException : Exception
{
    public ReadBackoffException()
    {
    }

    public ReadBackoffException(string message)
        : base(message)
    {
    }

    public ReadBackoffException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
