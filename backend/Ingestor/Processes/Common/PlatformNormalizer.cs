namespace Ingestor.Processes.Common;

/// <summary>
/// Normalizes configured platform identifiers into a clean, deduplicated list.
/// </summary>
public static class PlatformNormalizer
{
    /// <summary>
    /// Trims and upper-cases each configured platform, dropping blank entries and
    /// removing ordinal duplicates while preserving the original order.
    /// </summary>
    /// <param name="platforms">The configured platform identifiers.</param>
    /// <returns>The normalized, deduplicated platform identifiers.</returns>
    public static List<string> Normalize(IEnumerable<string> platforms)
    {
        return platforms
            .Where(platform => !string.IsNullOrWhiteSpace(platform))
            .Select(platform => platform.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
