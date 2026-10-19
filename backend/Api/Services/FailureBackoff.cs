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
    /// <see cref="ReadBackoffException"/> carrying that failure and the time left before the
    /// read is attempted again, without running it.
    /// </summary>
    public static async Task<T> RunAsync<T>(
        IMemoryCache cache,
        string key,
        TimeSpan backoff,
        Func<Task<T>> compute,
        TimeProvider? clock = null)
    {
        clock ??= TimeProvider.System;
        var failureKey = $"{key}:failed";
        if (cache.TryGetValue(failureKey, out RecordedFailure? recorded) && recorded is not null)
        {
            var retryAfter = recorded.RetryAtUtc - clock.GetUtcNow();
            throw new ReadBackoffException(
                "This read failed recently and is not retried until its backoff window ends.",
                recorded.Failure,
                retryAfter > TimeSpan.Zero ? retryAfter : TimeSpan.Zero);
        }

        try
        {
            return await compute();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            cache.Store(failureKey, new RecordedFailure(ex, clock.GetUtcNow() + backoff), backoff);
            throw;
        }
    }

    /// <summary>The failure that opened a window, and when the window closes.</summary>
    private sealed record RecordedFailure(Exception Failure, DateTimeOffset RetryAtUtc);
}

/// <summary>
/// Thrown instead of re-running a read that failed within its backoff window; the failure
/// that opened the window is the inner exception. <see cref="TrueMain.Http.ReadBackoffExceptionFilter"/>
/// answers it with a 503 whose <c>Retry-After</c> is <see cref="RetryAfter"/>, so a caller
/// can tell "try again shortly" from an unexpected 500.
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

    public ReadBackoffException(string message, Exception innerException, TimeSpan retryAfter)
        : base(message, innerException)
    {
        RetryAfter = retryAfter;
    }

    /// <summary>Time left before the read is attempted again.</summary>
    public TimeSpan RetryAfter { get; }
}
