namespace TrueMain.Services.Champions;

/// <summary>
/// Cache + single-flight for the champion reads, keyed by aggregation version.
/// See <see cref="ChampionReadCache"/> for why the two are one thing.
/// </summary>
public interface IChampionReadCache
{
    /// <summary>
    /// Returns the cached answer for <paramref name="key"/>, joins the pass already
    /// computing it, or runs <paramref name="compute"/> once on behalf of everyone
    /// waiting.
    /// </summary>
    /// <param name="key">
    /// Identifies the answer, and must carry every input that changes it — champion,
    /// lane, patch, elo bracket, population. The aggregation version is added by the
    /// implementation; callers must not add a timestamp of their own.
    /// </param>
    /// <param name="compute">
    /// The read. Invoked at most once per concurrent group, and with a token that is
    /// never a single caller's: the result is shared, so one request walking away must
    /// not cancel it for the others.
    /// </param>
    /// <param name="ct">The calling request's token.</param>
    /// <param name="size">
    /// What the entry charges against the shared cache's <c>SizeLimit</c>. One unit —
    /// "roughly one response" — unless the value is something else entirely.
    /// </param>
    Task<T> GetOrComputeAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> compute,
        CancellationToken ct,
        int size = 1);
}
