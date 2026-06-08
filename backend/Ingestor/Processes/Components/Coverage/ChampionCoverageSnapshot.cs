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
    private readonly bool _isNeutral;

    public ChampionCoverageSnapshot(IReadOnlyDictionary<int, int> mainsByChampion, int targetMainsPerChampion)
    {
        ArgumentNullException.ThrowIfNull(mainsByChampion);

        // An empty dictionary is NOT the neutral case — it would make Deficit() return 1.0 for
        // every champion (the opposite of Empty). Force callers to use Empty for "no signal".
        if (mainsByChampion.Count == 0)
        {
            throw new ArgumentException(
                "Use ChampionCoverageSnapshot.Empty for the no-signal case instead of an empty dictionary.",
                nameof(mainsByChampion));
        }

        _mainsByChampion = mainsByChampion;
        _targetMainsPerChampion = Math.Max(1, targetMainsPerChampion);
    }

    private ChampionCoverageSnapshot()
    {
        _mainsByChampion = new Dictionary<int, int>();
        _targetMainsPerChampion = 1;
        _isNeutral = true;
    }

    /// <summary>
    /// Explicit neutral snapshot for when there is no coverage signal to act on
    /// (cold start before any mains exist, or in tests): every deficit is 0, so callers
    /// keep their defaults (base threshold, no scoring bonus). This is intentionally a
    /// distinct state from a populated snapshot, not inferred from an empty dictionary.
    /// </summary>
    public static ChampionCoverageSnapshot Empty { get; } = new();

    public int MainsFor(int championId)
        => _mainsByChampion.TryGetValue(championId, out var count) ? count : 0;

    /// <summary>
    /// Scarcity in [0, 1]: 1 = no mains at all, 0 = at or above the target.
    /// A neutral snapshot always returns 0.
    /// </summary>
    public double Deficit(int championId)
    {
        if (_isNeutral)
        {
            return 0;
        }

        var deficit = (_targetMainsPerChampion - MainsFor(championId)) / (double)_targetMainsPerChampion;
        return Math.Clamp(deficit, 0, 1);
    }
}
