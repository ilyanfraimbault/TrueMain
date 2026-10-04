namespace Data.Repositories;

/// <summary>
/// Normalises a caller-supplied per-platform map to the platform-id form the claim compares
/// against (trimmed, upper-case), so a quota and a share for the same platform cannot miss each
/// other on casing. Blank keys are dropped; keys that collide after normalisation are merged
/// by <c>merge</c>.
/// </summary>
internal static class PlatformKeyed
{
    public static Dictionary<string, TValue> Normalize<TValue>(
        IReadOnlyDictionary<string, TValue> source,
        Func<IEnumerable<TValue>, TValue> merge)
        => source
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .GroupBy(entry => entry.Key.Trim().ToUpperInvariant(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => merge(group.Select(entry => entry.Value)), StringComparer.Ordinal);
}
