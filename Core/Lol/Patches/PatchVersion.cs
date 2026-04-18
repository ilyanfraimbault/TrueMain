using System.Globalization;

namespace Core.Lol.Patches;

/// <summary>
/// Value object representing a League of Legends patch in canonical "MAJOR.MINOR" form.
/// </summary>
public readonly record struct PatchVersion(int Major, int Minor) : IComparable<PatchVersion>
{
    public static PatchVersion Parse(string value)
    {
        if (!TryParse(value, out var version))
        {
            throw new ArgumentException($"Invalid patch version: '{value}'.", nameof(value));
        }

        return version;
    }

    public static bool TryParse(string? value, out PatchVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var segments = value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2
            || !int.TryParse(segments[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(segments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minor))
        {
            return false;
        }

        version = new PatchVersion(major, minor);
        return true;
    }

    /// <summary>
    /// String-to-string normalization mirroring the legacy behaviour:
    /// null / whitespace → empty string, single non-numeric segment → pass-through,
    /// any input with at least 2 dot-separated segments → "MAJOR.MINOR".
    /// </summary>
    public static string Normalize(string? gameVersion)
    {
        if (string.IsNullOrWhiteSpace(gameVersion))
        {
            return string.Empty;
        }

        var segments = gameVersion.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length >= 2 ? $"{segments[0]}.{segments[1]}" : gameVersion;
    }

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}");

    public int CompareTo(PatchVersion other)
    {
        var majorComparison = Major.CompareTo(other.Major);
        return majorComparison != 0 ? majorComparison : Minor.CompareTo(other.Minor);
    }

    public static bool operator <(PatchVersion left, PatchVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(PatchVersion left, PatchVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(PatchVersion left, PatchVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(PatchVersion left, PatchVersion right) => left.CompareTo(right) >= 0;
}
