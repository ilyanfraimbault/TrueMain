namespace Core;

/// <summary>
/// Provides utility methods for handling Riot API data conversions and parsing.
/// </summary>
public static class RiotDataHelpers
{
    /// <summary>
    /// Attempts to parse a platform string into a <see cref="PlatformRoute"/> enum value.
    /// </summary>
    /// <param name="platform">The platform string to parse (e.g., "KR", "EUW1", "NA1").</param>
    /// <param name="route">When this method returns, contains the parsed <see cref="PlatformRoute"/> value if parsing succeeded.</param>
    /// <returns><c>true</c> if the platform string was successfully parsed; otherwise, <c>false</c>.</returns>
    public static bool TryParsePlatform(string? platform, out PlatformRoute route)
    {
        route = default;
        if (string.IsNullOrWhiteSpace(platform))
        {
            return false;
        }

        return Enum.TryParse(platform.Trim(), ignoreCase: true, out route);
    }

    /// <summary>
    /// Converts a Unix timestamp in milliseconds to a UTC <see cref="DateTime"/>.
    /// </summary>
    /// <param name="timestampMs">The Unix timestamp in milliseconds.</param>
    /// <returns>
    /// A <see cref="DateTime"/> in UTC if the timestamp is valid (greater than 0); otherwise, <c>null</c>.
    /// </returns>
    public static DateTime? ToUtcDateTime(long timestampMs)
    {
        if (timestampMs <= 0)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(timestampMs).UtcDateTime;
    }

    /// <summary>
    /// Safely converts a <see cref="long"/> value to an <see cref="int"/>.
    /// </summary>
    /// <param name="value">The long value to convert.</param>
    /// <returns>
    /// 0 if the value is less than or equal to 0;
    /// <see cref="int.MaxValue"/> if the value exceeds <see cref="int.MaxValue"/>;
    /// otherwise, the value cast to an <see cref="int"/>.
    /// </returns>
    public static int ToIntSafe(long value)
    {
        if (value <= 0)
        {
            return 0;
        }

        return value > int.MaxValue ? int.MaxValue : (int)value;
    }
}
