using Data.Pipeline;

namespace Ingestor.Options;

public static class JobModeParser
{
    /// <summary>
    /// Parses a configured Job:Mode string into a <see cref="JobMode"/>: an enum name,
    /// case-insensitively, or null/empty for <see cref="JobMode.Full"/>. Numeric strings are
    /// rejected, so only a named mode resolves.
    /// </summary>
    public static bool TryParse(string? value, out JobMode mode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            mode = JobMode.Full;
            return true;
        }

        var name = Array.Find(
            Enum.GetNames<JobMode>(),
            candidate => string.Equals(candidate, value.Trim(), StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            mode = default;
            return false;
        }

        mode = Enum.Parse<JobMode>(name);
        return true;
    }

    public static JobMode Parse(string? value)
        => TryParse(value, out var mode)
            ? mode
            : throw new InvalidOperationException($"Invalid Job:Mode '{value}'.");
}
