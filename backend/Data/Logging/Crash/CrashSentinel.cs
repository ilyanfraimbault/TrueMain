using System.Reflection;
using System.Text.Json;
using Data.Logging.Mongo;

namespace Data.Logging.Crash;

/// <summary>
/// The last-known state of a process, persisted to a small JSON sentinel file next
/// to the crash log. A run that ended <see cref="CrashSentinel.StatusStopped"/> shut
/// down (or crashed and recorded) cleanly; a run still
/// <see cref="CrashSentinel.StatusRunning"/> at the next boot vanished without a
/// graceful shutdown — the signature of an OOM kill / SIGKILL / hard crash.
/// </summary>
public sealed record CrashSentinelState
{
    public string Status { get; init; } = string.Empty;

    public string? ProcessName { get; init; }

    public DateTime StartedAtUtc { get; init; }

    /// <summary>Refreshed periodically while the process lives; its last value is the moment-of-death proxy.</summary>
    public DateTime LastSeenUtc { get; init; }

    public long WorkingSetBytes { get; init; }

    public long TotalManagedMemoryBytes { get; init; }

    public int Gen0Collections { get; init; }

    public int Gen1Collections { get; init; }

    public int Gen2Collections { get; init; }

    /// <summary>Optional free-form note (e.g. the Ingestor iteration in flight).</summary>
    public string? LastContext { get; init; }
}

/// <summary>
/// Reads and writes the per-process crash sentinel. The sentinel is what lets the
/// next boot detect a death that no in-process hook could catch (OOM/SIGKILL):
/// <see cref="MarkRunning"/> on start, periodic <see cref="Refresh"/> to keep the
/// memory snapshot current, and <see cref="MarkStopped"/> on a graceful shutdown or
/// a recorded crash. All writes are best-effort and never throw.
/// </summary>
internal static class CrashSentinel
{
    public const string StatusRunning = "running";
    public const string StatusStopped = "stopped";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly object Gate = new();

    private static bool _finalized;
    private static DateTime _startedAtUtc;
    private static string? _lastContext;

    public static string ResolveProcessName(MongoLoggingOptions options)
        => string.IsNullOrWhiteSpace(options.ProcessName)
            ? Assembly.GetEntryAssembly()?.GetName().Name ?? "Process"
            : options.ProcessName;

    /// <summary>Reads the PREVIOUS run's sentinel. Call before <see cref="MarkRunning"/> overwrites it.</summary>
    public static CrashSentinelState? ReadPrevious(MongoLoggingOptions options, string processName)
    {
        try
        {
            var path = SentinelPath(options, processName);
            if (path is null || !File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<CrashSentinelState>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            ReportError($"[CrashSentinel] failed to read previous sentinel: {ex}");
            return null;
        }
    }

    public static void MarkRunning(MongoLoggingOptions options, string processName, string? context = null)
    {
        lock (Gate)
        {
            _finalized = false;
            _startedAtUtc = DateTime.UtcNow;
            _lastContext = context;
            Write(options, processName, StatusRunning);
        }
    }

    /// <summary>Updates <c>lastSeenUtc</c> + the live memory snapshot. No-op once stopped.</summary>
    public static void Refresh(MongoLoggingOptions options, string processName)
    {
        lock (Gate)
        {
            if (_finalized)
            {
                return;
            }

            Write(options, processName, StatusRunning);
        }
    }

    public static void SetContext(string? context)
    {
        lock (Gate)
        {
            _lastContext = context;
        }
    }

    public static void MarkStopped(MongoLoggingOptions options, string processName)
    {
        lock (Gate)
        {
            _finalized = true;
            Write(options, processName, StatusStopped);
        }
    }

    private static void Write(MongoLoggingOptions options, string processName, string status)
    {
        try
        {
            var path = SentinelPath(options, processName);
            if (path is null)
            {
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            long workingSet = 0;
            long managed = 0;
            int gen0 = 0;
            int gen1 = 0;
            int gen2 = 0;
            try
            {
                workingSet = Environment.WorkingSet;
                managed = GC.GetTotalMemory(forceFullCollection: false);
                gen0 = GC.CollectionCount(0);
                gen1 = GC.CollectionCount(1);
                gen2 = GC.CollectionCount(2);
            }
            catch
            {
                // Memory probes are best-effort; a failure leaves the zeros above.
            }

            var state = new CrashSentinelState
            {
                Status = status,
                ProcessName = processName,
                StartedAtUtc = _startedAtUtc == default ? DateTime.UtcNow : _startedAtUtc,
                LastSeenUtc = DateTime.UtcNow,
                WorkingSetBytes = workingSet,
                TotalManagedMemoryBytes = managed,
                Gen0Collections = gen0,
                Gen1Collections = gen1,
                Gen2Collections = gen2,
                LastContext = _lastContext
            };

            // Write to a temp file then move, so a crash mid-write never leaves a
            // truncated sentinel that would fail to parse on the next boot.
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            ReportError($"[CrashSentinel] failed to write sentinel: {ex}");
        }
    }

    private static string? SentinelPath(MongoLoggingOptions options, string processName)
    {
        var dir = options.CrashFilePath;
        return string.IsNullOrWhiteSpace(dir)
            ? null
            : Path.Combine(dir, $"{processName}.sentinel.json");
    }

    private static void ReportError(string message)
    {
        try
        {
            Console.Error.WriteLine(message);
        }
        catch
        {
            // Nothing safe left to do.
        }
    }
}
