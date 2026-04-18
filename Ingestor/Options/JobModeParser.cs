namespace Ingestor.Options;

public static class JobModeParser
{
    /// <summary>
    /// Parses a configured Job:Mode string into a <see cref="JobMode"/>.
    /// Accepts the enum names case-insensitively, the legacy
    /// <c>RetentionOnly</c> alias for <see cref="JobMode.MatchDataRetentionOnly"/>,
    /// and a null/empty value (defaults to <see cref="JobMode.Full"/>).
    /// </summary>
    public static bool TryParse(string? value, out JobMode mode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            mode = JobMode.Full;
            return true;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, "RetentionOnly", StringComparison.OrdinalIgnoreCase))
        {
            mode = JobMode.MatchDataRetentionOnly;
            return true;
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out mode) && Enum.IsDefined(mode);
    }

    public static JobMode Parse(string? value)
        => TryParse(value, out var mode)
            ? mode
            : throw new InvalidOperationException($"Invalid Job:Mode '{value}'.");
}
