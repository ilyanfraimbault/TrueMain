using Data.Configuration;

namespace Data.Ops.Mongo;

/// <summary>
/// The channel a process uses to tell the admin portal what it is running with (#1034): the
/// Ingestor writes its snapshot at boot, the Api reads every published snapshot back.
/// </summary>
public interface IEffectiveConfigurationStore
{
    /// <summary>
    /// Publishes <paramref name="snapshot"/>, replacing whatever this process published before.
    /// Returns false when Mongo is not configured — the whole logging stack degrades to a no-op
    /// in that case, and publishing configuration must not be the one thing that throws.
    /// </summary>
    Task<bool> UpsertAsync(EffectiveConfigurationSnapshot snapshot, CancellationToken ct);

    /// <summary>
    /// Every published snapshot, one per process. Empty when Mongo is not configured or when
    /// nothing has published yet — the caller reports the gap rather than rendering half a page.
    /// </summary>
    Task<IReadOnlyList<EffectiveConfigurationSnapshot>> GetAllAsync(CancellationToken ct);
}
