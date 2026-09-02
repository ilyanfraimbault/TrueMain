using System.Collections.Concurrent;

namespace TrueMain.Services;

/// <summary>
/// Single-flight for expensive read paths: concurrent callers asking for the same key
/// share one in-flight computation instead of each running their own (#870).
///
/// <para>
/// The problem it solves is the cache-miss stampede. A cached value protects steady
/// state, but the moment it expires — or on the first request of the day — every
/// concurrent caller sees a miss at once and starts the same scan. The dedication
/// ranking is the sharp case: one pass scores up to 50 000 accounts, so ten simultaneous
/// requests meant ten of those passes for one answer.
/// </para>
///
/// <para>
/// <b>A single-flight, not a lock.</b> A per-key lock would serialise the callers and
/// still run the work N times whenever the result cannot be cached (the ranking cache
/// rejects entries above its size budget), turning a burst into a queue of full scans.
/// Sharing the <see cref="Task{TResult}"/> gives every waiter the same result from one
/// pass, whether or not it ends up cached.
/// </para>
///
/// <para>
/// <b>Cancellation is per caller.</b> The shared work runs detached from any one
/// request's token: a caller who walks away must not cancel the pass the others are
/// waiting on. Each caller awaits with its own token, so it returns promptly on
/// cancellation while the shared computation finishes for everyone else. The bound on
/// that detached work is the caller's own — the ranking is capped and short-lived —
/// so this is not a place to start something unbounded.
/// </para>
/// </summary>
internal sealed class RequestCoalescer<TValue>
{
    private readonly ConcurrentDictionary<string, Lazy<Task<TValue>>> _inFlight = new(StringComparer.Ordinal);

    /// <summary>
    /// Runs <paramref name="factory"/> for <paramref name="key"/>, or joins the pass
    /// already running for it.
    /// </summary>
    /// <param name="key">Identifies the computation; callers sharing a key share a pass.</param>
    /// <param name="factory">
    /// The work. Invoked at most once per concurrent group, and never with a caller's
    /// cancellation token — see the type remarks.
    /// </param>
    /// <param name="ct">The calling request's token. Abandons the wait, not the work.</param>
    /// <param name="ownerAwaitsToCompletion">
    /// When set, the caller that <em>started</em> the pass ignores its own token and
    /// waits for the result; only the joiners can walk away. This is for work that
    /// borrows something scoped to the owning request — a request-scoped
    /// <c>DbContext</c>, say. There the owner abandoning its wait is not merely
    /// wasteful, it is fatal to everyone else: its scope is disposed the moment its
    /// request unwinds, and the shared pass then dies on a disposed context, failing
    /// every joiner with it. Cost of holding on: one aborted request's async
    /// continuation lives until the work finishes, bounded by the same command timeout
    /// that bounds the work itself. Leave it off when the factory owns everything it
    /// touches (the leaderboard's ranking creates its own context).
    /// </param>
    public async Task<TValue> GetOrJoinAsync(
        string key,
        Func<Task<TValue>> factory,
        CancellationToken ct,
        bool ownerAwaitsToCompletion = false)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        // Allocated before the lookup so the winner is decided by the dictionary rather
        // than by a factory delegate that ConcurrentDictionary may run more than once.
        // The loser's Lazy never has its Value read, so it never starts any work.
        var candidate = new Lazy<Task<TValue>>(factory, LazyThreadSafetyMode.ExecutionAndPublication);
        var inFlight = _inFlight.GetOrAdd(key, candidate);

        if (ReferenceEquals(inFlight, candidate))
        {
            // Only the caller that installed the entry owns its removal, and it removes
            // by (key, value) so it can never evict a later group's pass. Doing this on
            // completion rather than in the callers' finally blocks matters: if every
            // waiter cancels, the entry would otherwise be stranded holding a completed
            // result for ever.
            _ = inFlight.Value.ContinueWith(
                _ => _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<TValue>>>(key, candidate)),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            if (ownerAwaitsToCompletion)
            {
                return await inFlight.Value;
            }
        }

        return await inFlight.Value.WaitAsync(ct);
    }

    /// <summary>Passes currently in flight. For tests and diagnostics.</summary>
    public int InFlightCount => _inFlight.Count;
}
