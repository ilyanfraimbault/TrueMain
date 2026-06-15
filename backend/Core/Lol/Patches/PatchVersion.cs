using System.Globalization;

namespace Core.Lol.Patches;

/// <summary>
/// Value object representing a League of Legends patch in canonical
/// "MAJOR.MINOR" form, optionally carrying Riot's hotfix build segment
/// (the "521" in "16.4.521") when one is present.
/// </summary>
public readonly record struct PatchVersion(int Major, int Minor, int? Build = null) : IComparable<PatchVersion>
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

        // Riot appends a hotfix build ("16.4.521.x"); capture the third segment
        // when it is numeric. A non-numeric third segment is ignored rather than
        // failing the parse, preserving the legacy "major.minor wins" behaviour.
        int? build = null;
        if (segments.Length >= 3
            && int.TryParse(segments[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBuild))
        {
            build = parsedBuild;
        }

        version = new PatchVersion(major, minor, build);
        return true;
    }

    /// <summary>
    /// String-to-string normalization mirroring the legacy behaviour, kept for
    /// callers that persist or compare patch strings without parsing them:
    /// <list type="bullet">
    ///   <item>null / empty / whitespace input → empty string;</item>
    ///   <item>input with 2+ dot-separated segments → first two segments joined
    ///   ("16.4.521.123" → "16.4"); segments are not validated as numeric;</item>
    ///   <item>input with a single segment after splitting (e.g. "16", "abc")
    ///   → original input returned unchanged.</item>
    /// </list>
    /// Use <see cref="Parse"/> / <see cref="TryParse"/> when a strictly typed
    /// <see cref="PatchVersion"/> is needed.
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
        => Build is { } build
            ? string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{build}")
            : string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}");

    public int CompareTo(PatchVersion other)
    {
        var majorComparison = Major.CompareTo(other.Major);
        if (majorComparison != 0)
        {
            return majorComparison;
        }

        var minorComparison = Minor.CompareTo(other.Minor);
        // A patch without a build sorts before its hotfixes; Nullable.Compare
        // treats null as the smallest value, matching that "base before hotfix".
        return minorComparison != 0 ? minorComparison : Nullable.Compare(Build, other.Build);
    }

    public static bool operator <(PatchVersion left, PatchVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(PatchVersion left, PatchVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(PatchVersion left, PatchVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(PatchVersion left, PatchVersion right) => left.CompareTo(right) >= 0;
}
