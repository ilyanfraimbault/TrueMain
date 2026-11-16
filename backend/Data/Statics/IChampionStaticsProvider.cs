namespace Data.Statics;

/// <summary>
/// Per-patch champion statics (attack range today), keyed by numeric champion id.
/// </summary>
public interface IChampionStaticsProvider
{
    /// <summary>
    /// The champions of the patch <paramref name="gameVersion"/> belongs to, or why there are
    /// none (<see cref="StaticData{T}"/>). Never throws but on cancellation.
    /// </summary>
    Task<StaticData<IReadOnlyDictionary<int, ChampionStatics>>> GetChampionsAsync(string gameVersion, CancellationToken ct);
}
