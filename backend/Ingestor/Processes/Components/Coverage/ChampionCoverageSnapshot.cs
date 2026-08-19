namespace Ingestor.Processes.Components.Coverage;

/// <summary>
/// Immutable per-platform, per-champion coverage snapshot taken once at the start of a
/// cycle. Candidate scoring (intake), main classification (retention) and the match-ingest
/// claim (budget) read the same <see cref="Deficit"/> signal, so they stay coherent.
/// Freezing the snapshot per cycle keeps the feedback loop self-damping rather than
/// oscillating: as a champion gains mains, its deficit shrinks on the next cycle, which
/// automatically tapers the scoring bonus, the threshold relaxation and the extra claim
/// budget its platform was getting.
///
/// <para>
/// Keyed by (platform, champion) rather than by champion alone (#1150). A champion-only
/// count is dominated by whichever region we happen to have ingested most, so a champion
/// with 60 mains on EUW1 and 1 on KR read as fully covered and every under-served region
/// got a zero bonus — the one signal that could have damped the region imbalance was blind
/// to it. Coverage is a per-region question because a region's champion pool is what its
/// own stats are computed from.
/// </para>
/// </summary>
public sealed class ChampionCoverageSnapshot
{
    private readonly IReadOnlyDictionary<(string PlatformId, int ChampionId), int> _mainsByPlatformChampion;
    private readonly int _targetMainsPerChampion;
    private readonly bool _isNeutral;

    public ChampionCoverageSnapshot(
        IReadOnlyDictionary<(string PlatformId, int ChampionId), int> mainsByPlatformChampion,
        int targetMainsPerChampion)
    {
        ArgumentNullException.ThrowIfNull(mainsByPlatformChampion);

        // An empty dictionary is NOT the neutral case — it would make Deficit() return 1.0 for
        // every champion (the opposite of Empty). Force callers to use Empty for "no signal".
        if (mainsByPlatformChampion.Count == 0)
        {
            throw new ArgumentException(
                "Use ChampionCoverageSnapshot.Empty for the no-signal case instead of an empty dictionary.",
                nameof(mainsByPlatformChampion));
        }

        // Re-keyed through a platform-case-insensitive comparer rather than stored as given.
        // A raw tuple dictionary compares its string component ordinally, so a lookup keyed
        // "euw1" would miss a row stored as "EUW1" and report zero mains — i.e. a *maximal*
        // deficit — with nothing to indicate anything went wrong. Every caller happens to pass
        // canonical upper-case ids today, but the allocator's platform list comes from
        // configuration, and a signal that silently inverts on a lower-cased config entry is
        // not one to leave load-bearing.
        _mainsByPlatformChampion = mainsByPlatformChampion.ToDictionary(
            pair => pair.Key, pair => pair.Value, PlatformChampionComparer.Instance);
        _targetMainsPerChampion = Math.Max(1, targetMainsPerChampion);

        // The champion universe is the union across platforms, not each platform's own keys:
        // a champion with zero mains on KR has no key there, and that absence is exactly the
        // deficit we want counted. Taking the union means "known to exist somewhere we track"
        // rather than hard-coding the roster, so a newly released champion enters the
        // denominator as soon as it has a single main anywhere.
        ChampionIds = mainsByPlatformChampion.Keys.Select(key => key.ChampionId).ToHashSet();

        SaturatedChampionIdsByPlatform = mainsByPlatformChampion.Keys
            .Select(key => key.PlatformId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                platformId => platformId,
                platformId => (IReadOnlySet<int>)ChampionIds
                    .Where(championId => MainsFor(platformId, championId) >= _targetMainsPerChampion)
                    .ToHashSet(),
                StringComparer.OrdinalIgnoreCase);
    }

    private ChampionCoverageSnapshot()
    {
        _mainsByPlatformChampion = new Dictionary<(string, int), int>(PlatformChampionComparer.Instance);
        _targetMainsPerChampion = 1;
        _isNeutral = true;
        ChampionIds = new HashSet<int>();
        SaturatedChampionIdsByPlatform = new Dictionary<string, IReadOnlySet<int>>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Explicit neutral snapshot for when there is no coverage signal to act on
    /// (cold start before any mains exist, or in tests): every deficit is 0, so callers
    /// keep their defaults (base threshold, no scoring bonus, an even claim split). This is
    /// intentionally a distinct state from a populated snapshot, not inferred from an empty
    /// dictionary.
    /// </summary>
    public static ChampionCoverageSnapshot Empty { get; } = new();

    /// <summary>
    /// Every champion that holds at least one active main on at least one tracked platform —
    /// the denominator for <see cref="MeanDeficit"/>. Empty on a neutral snapshot.
    /// </summary>
    public IReadOnlySet<int> ChampionIds { get; }

    /// <summary>
    /// Per platform, the champions that already hold at least the target number of active
    /// mains <em>on that platform</em> (#900, region-scoped by #1150). Their candidates only
    /// take the leftover slots of that platform's promotion queue: past the target, another
    /// main on the same champion is worth less than more games from the mains already
    /// tracked. Empty on a neutral snapshot.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlySet<int>> SaturatedChampionIdsByPlatform { get; }

    /// <summary>
    /// The champions saturated on <paramref name="platformId"/>, or an empty set when the
    /// platform holds no mains at all (in which case nothing is saturated there).
    /// </summary>
    public IReadOnlySet<int> SaturatedChampionIdsFor(string platformId)
        => SaturatedChampionIdsByPlatform.TryGetValue(platformId, out var saturated)
            ? saturated
            : new HashSet<int>();

    // Internal on purpose: consumers should depend on the normalised Deficit() signal,
    // not on raw counts (where an absent key and a 0 count both mean "no mains").
    internal int MainsFor(string platformId, int championId)
        => _mainsByPlatformChampion.TryGetValue((platformId, championId), out var count) ? count : 0;

    /// <summary>
    /// Scarcity of <paramref name="championId"/> on <paramref name="platformId"/>, in [0, 1]:
    /// 1 = no mains at all there, 0 = at or above the target. A neutral snapshot always
    /// returns 0.
    /// </summary>
    public double Deficit(string platformId, int championId)
    {
        if (_isNeutral)
        {
            return 0;
        }

        var deficit = (_targetMainsPerChampion - MainsFor(platformId, championId)) / (double)_targetMainsPerChampion;
        return Math.Clamp(deficit, 0, 1);
    }

    /// <summary>
    /// How under-covered a platform is overall, in [0, 1]: the mean per-champion deficit over
    /// the whole known champion pool. 0 = every champion is at target there, 1 = the platform
    /// has no mains at all.
    /// <para>
    /// This is the region-balance signal (#1150). It is deliberately a mean over the shared
    /// champion universe rather than over the platform's own keys: a region missing a champion
    /// entirely is the strongest possible deficit, and averaging only over what it already has
    /// would score that region as perfectly covered.
    /// </para>
    /// </summary>
    public double MeanDeficit(string platformId)
    {
        if (_isNeutral || ChampionIds.Count == 0)
        {
            return 0;
        }

        var total = 0d;
        foreach (var championId in ChampionIds)
        {
            total += Deficit(platformId, championId);
        }

        return Math.Clamp(total / ChampionIds.Count, 0, 1);
    }

    /// <summary>
    /// Compares (platform, champion) keys with the platform case-insensitively, so the map
    /// agrees with <see cref="SaturatedChampionIdsByPlatform"/> and with every other
    /// platform-keyed lookup in the pipeline.
    /// </summary>
    private sealed class PlatformChampionComparer : IEqualityComparer<(string PlatformId, int ChampionId)>
    {
        public static readonly PlatformChampionComparer Instance = new();

        public bool Equals((string PlatformId, int ChampionId) x, (string PlatformId, int ChampionId) y)
            => x.ChampionId == y.ChampionId
               && StringComparer.OrdinalIgnoreCase.Equals(x.PlatformId, y.PlatformId);

        public int GetHashCode((string PlatformId, int ChampionId) obj)
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.PlatformId), obj.ChampionId);
    }
}
