using System.Globalization;

namespace Ingestor.FetchWorkers;

/// <summary>
/// The host resources the match-fetch workers brake on (#1459), as percentages. A null reading
/// is a resource this host cannot report, which then applies no backpressure.
/// </summary>
/// <param name="AvailableMemoryPercent"><c>MemAvailable</c> over <c>MemTotal</c>, in percent.</param>
/// <param name="FreeDiskPercent">The free share of the watched disk, in percent.</param>
/// <param name="Failure">
/// Why a resource the host does have could not be read, when that happened: unlike a host that
/// simply has no <c>/proc/meminfo</c>, a failed read silently disables a safety brake, so it is
/// reported as an error rather than taken as "nothing to report".
/// </param>
public readonly record struct HostResourceReading(
    double? AvailableMemoryPercent,
    double? FreeDiskPercent,
    string? Failure = null);

/// <summary>Reads the host resources the backpressure is computed from.</summary>
public interface IHostResourceProbe
{
    /// <summary>Reads memory and the free space of the disk holding <paramref name="diskPath"/>.</summary>
    HostResourceReading Read(string diskPath);
}

/// <summary>
/// <see cref="IHostResourceProbe"/> over <c>/proc/meminfo</c> and the file system. Inside a
/// container, <c>/proc/meminfo</c> reports the host's memory — the machine the database shares
/// with the ingestor, which is what ran out in #600 — and the root file system sits on the
/// host's Docker data root, the disk that filled in #680.
/// </summary>
public sealed class HostResourceProbe : IHostResourceProbe
{
    private const string MemInfoPath = "/proc/meminfo";

    /// <inheritdoc />
    public HostResourceReading Read(string diskPath)
    {
        var (memory, memoryFailure) = ReadAvailableMemoryPercent();
        var (disk, diskFailure) = ReadFreeDiskPercent(diskPath);
        var failures = new[] { memoryFailure, diskFailure }.OfType<string>().ToArray();
        return new HostResourceReading(memory, disk, failures.Length == 0 ? null : string.Join("; ", failures));
    }

    /// <summary>The available share of memory in a <c>/proc/meminfo</c> text, or null when it carries none.</summary>
    public static double? ParseAvailableMemoryPercent(string memInfo)
    {
        long? total = null;
        long? available = null;
        foreach (var line in memInfo.Split('\n'))
        {
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
            {
                total = ParseKilobytes(line);
            }
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
            {
                available = ParseKilobytes(line);
            }
        }

        return total is > 0 && available is { } free ? 100d * free / total.Value : null;
    }

    private static (double? Percent, string? Failure) ReadAvailableMemoryPercent()
    {
        if (!File.Exists(MemInfoPath))
        {
            // Not Linux (a dev machine): no memory brake, and nothing went wrong.
            return (null, null);
        }

        try
        {
            return ParseAvailableMemoryPercent(File.ReadAllText(MemInfoPath)) is { } percent
                ? (percent, null)
                : (null, $"{MemInfoPath} carries no MemTotal/MemAvailable");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, $"{MemInfoPath}: {ex.Message}");
        }
    }

    private static (double? Percent, string? Failure) ReadFreeDiskPercent(string diskPath)
    {
        try
        {
            var drive = new DriveInfo(Path.GetFullPath(diskPath));
            return drive.TotalSize > 0
                ? (100d * drive.AvailableFreeSpace / drive.TotalSize, null)
                : (null, $"{diskPath}: the disk reports no size");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return (null, $"{diskPath}: {ex.Message}");
        }
    }

    private static long? ParseKilobytes(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
