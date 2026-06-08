namespace Ingestor.Processes.Components.Coverage;

/// <summary>
/// Immutable per-champion coverage snapshot taken once at the start of a cycle.
/// Candidate scoring (intake) and main classification (retention) read the same
/// <see cref="Deficit"/> signal, so they stay coherent. Freezing the snapshot per
/// cycle keeps the feedback loop self-damping rather than oscillating: as a champion
/// gains mains, its deficit shrinks on the next cycle, which automatically tapers
/// both the scoring bonus and the threshold relaxation.
/// </summary>
public sealed class ChampionCoverageSnapshot
{
    private readonly IReadOnlyDictionary<int, int> _mainsByChampion;
    private readonly int _targetMainsPerChampion;

    public ChampionCoverageSnapshot(IReadOnlyDictionary<int, int> mainsByChampion, int targetMainsPerChampion)
    {
        _mainsByChampion = mainsByChampion ?? throw new ArgumentNullException(nameof(mainsByChampion));
        _targetMainsPerChampion = Math.Max(1, targetMainsPerChampion);
    }

    /// <summary>
    /// A neutral snapshot carrying no coverage data: every deficit is 0, so callers
    /// fall back to their default behaviour (no scoring bonus, base threshold).
    /// </summary>
    public static ChampionCoverageSnapshot Empty { get; } = new(new Dictionary<int, int>(), 1);

    public int MainsFor(int championId)
        => _mainsByChampion.TryGetValue(championId, out var count) ? count : 0;

    /// <summary>
    /// Scarcity in [0, 1]: 1 = no mains at all, 0 = at or above the target.
    /// Returns 0 when the snapshot carries no data so callers keep their defaults.
    /// </summary>
    public double Deficit(int championId)
    {
        if (_mainsByChampion.Count == 0)
        {
            return 0;
        }

        var deficit = (_targetMainsPerChampion - MainsFor(championId)) / (double)_targetMainsPerChampion;
        return Math.Clamp(deficit, 0, 1);
    }
}
