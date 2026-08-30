namespace TrueMain.TestKit;

/// <summary>
/// Bounded polling for tests that wait on something asynchronous (a background sink
/// flushing, an in-flight count draining). Polling with a deadline, never a fixed sleep.
/// </summary>
public static class AsyncWait
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Polls <paramref name="condition"/> until it returns true, then returns. Throws
    /// <see cref="TimeoutException"/> when the deadline elapses first — the failure has to
    /// name the wait that timed out, otherwise the test carries on and blames whichever
    /// assertion happens to break next.
    /// </summary>
    public static async Task UntilAsync(
        Func<Task<bool>> condition,
        string description,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null)
    {
        TimeSpan resolvedTimeout = timeout ?? DefaultTimeout;
        TimeSpan resolvedInterval = pollInterval ?? DefaultPollInterval;
        DateTime deadline = DateTime.UtcNow + resolvedTimeout;

        while (true)
        {
            if (await condition())
            {
                return;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Timed out after {resolvedTimeout.TotalSeconds:0.##}s waiting for: {description}");
            }

            await Task.Delay(resolvedInterval);
        }
    }

    /// <summary>
    /// Synchronous-condition overload, for waits on a value a background task mutates
    /// in memory.
    /// </summary>
    public static Task UntilAsync(
        Func<bool> condition,
        string description,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null)
        => UntilAsync(() => Task.FromResult(condition()), description, timeout, pollInterval);
}
