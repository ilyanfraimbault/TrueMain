namespace Data.Statics;

/// <summary>
/// Per-patch champion statics (attack range today), keyed by numeric champion id.
/// </summary>
public interface IChampionStaticsProvider
{
    /// <summary>
    /// The champions of the patch <paramref name="gameVersion"/> belongs to. Throws when
    /// the source is unreachable — callers that can live without the data catch and
    /// carry on, as the profile fold does for its ranged flag.
    /// </summary>
    Task<IReadOnlyDictionary<int, ChampionStatics>> GetChampionsAsync(string gameVersion, CancellationToken ct);
}
