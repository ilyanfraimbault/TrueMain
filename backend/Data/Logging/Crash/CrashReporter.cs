using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Data.Logging.Mongo;
using Microsoft.Extensions.Options;

namespace Data.Logging.Crash;

/// <summary>
/// Durable, dual-sink crash writer. Builds a <see cref="CrashReport"/> and writes it
/// <b>file first</b> (the sink that survives Mongo being down) then to the Mongo
/// <c>crashes</c> collection with a short timeout, so a Mongo outage can never block
/// a dying process. Mirrors the lossless, synchronous insert of
/// <see cref="MongoAuditLog"/> rather than the lossy batched diagnostic channel.
/// </summary>
internal sealed class CrashReporter(
    MongoLogContext context,
    IOptions<MongoLoggingOptions> options,
    RecentLogTailProvider tail) : ICrashReporter
{
    private static readonly TimeSpan DedupeWindow = TimeSpan.FromMinutes(1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly MongoLoggingOptions _options = options.Value;

    private readonly object _dedupeGate = new();
    private Exception? _lastException;
    private DateTime _lastReportUtc;

    // Report can run on several threads at once; serialize the crash-file write so
    // concurrent appends can't collide and lose a line.
    private readonly object _fileGate = new();

    public bool IsEnabled => true;

    private string ProcessName => string.IsNullOrWhiteSpace(_options.ProcessName)
        ? Assembly.GetEntryAssembly()?.GetName().Name ?? "Process"
        : _options.ProcessName;

    public void Report(CrashSource source, Exception? exception, int? exitCode = null)
    {
        // A single death can reach us twice: AppDomain.UnhandledException fires on the
        // throwing thread, then the same exception propagates into the HostRun
        // try/catch. Collapse the duplicate by reference within a short window.
        if (exception is not null)
        {
            lock (_dedupeGate)
            {
                if (ReferenceEquals(_lastException, exception)
                    && DateTime.UtcNow - _lastReportUtc < DedupeWindow)
                {
                    return;
                }

                _lastException = exception;
                _lastReportUtc = DateTime.UtcNow;
            }
        }

        CrashReport report;
        try
        {
            report = BuildReport(source, exception, exitCode);
        }
        catch (Exception ex)
        {
            // Building the report must never be what kills the dying process.
            ReportError($"[CrashReporter] failed to build crash report: {ex}");
            return;
        }

        WriteToFile(report);
        WriteToMongo(report);

        // Terminal sources mean the process is going down. Mark the sentinel stopped
        // so the next boot does not ALSO emit a redundant UncleanShutdown for this
        // same death — we already captured a rich, stack-trace-carrying report here.
        if (source is CrashSource.AppDomainUnhandled or CrashSource.HostRun)
        {
            CrashSentinel.MarkStopped(_options, ProcessName);
        }
    }

    public void ReportUncleanShutdown(CrashSentinelState previous)
    {
        var ranForSeconds = previous.LastSeenUtc > previous.StartedAtUtc
            ? (previous.LastSeenUtc - previous.StartedAtUtc).TotalSeconds
            : 0;

        var message =
            $"Previous {ProcessName} run did not shut down cleanly (likely OOM kill / SIGKILL / hard crash). "
            + $"Started {previous.StartedAtUtc:O}, last seen {previous.LastSeenUtc:O} (ran ~{ranForSeconds:N0}s). "
            + $"Last-known working set {FormatMb(previous.WorkingSetBytes)}, managed heap {FormatMb(previous.TotalManagedMemoryBytes)}, "
            + $"GC gen0/1/2 {previous.Gen0Collections}/{previous.Gen1Collections}/{previous.Gen2Collections}."
            + (string.IsNullOrWhiteSpace(previous.LastContext) ? string.Empty : $" Last context: {previous.LastContext}.");

        var report = new CrashReport
        {
            Id = Guid.NewGuid(),
            TimestampUtc = DateTime.UtcNow,
            ProcessName = ProcessName,
            Source = CrashSource.UncleanShutdown,
            ExceptionType = null,
            Message = message,
            StackTrace = null,
            InnerExceptions = [],
            Host = SafeHost(),
            OsDescription = SafeOsDescription(),
            // The dead run's lifetime and memory profile, not this fresh process's.
            UptimeSeconds = ranForSeconds,
            RuntimeVersion = SafeRuntimeVersion(),
            AppVersion = SafeAppVersion(),
            WorkingSetBytes = previous.WorkingSetBytes,
            TotalManagedMemoryBytes = previous.TotalManagedMemoryBytes,
            Gen0Collections = previous.Gen0Collections,
            Gen1Collections = previous.Gen1Collections,
            Gen2Collections = previous.Gen2Collections,
            ExitCode = null,
            // The dead run's in-memory tail is gone; the current process's would be
            // misleading, so leave it empty. Its last warnings may still be in /ops/logs.
            RecentLogTail = []
        };

        WriteToFile(report);
        WriteToMongo(report);
    }

    private CrashReport BuildReport(CrashSource source, Exception? exception, int? exitCode) => new()
    {
        Id = Guid.NewGuid(),
        TimestampUtc = DateTime.UtcNow,
        ProcessName = ProcessName,
        Source = source,
        ExceptionType = exception?.GetType().FullName,
        Message = exception?.Message,
        StackTrace = exception?.StackTrace,
        InnerExceptions = FlattenInner(exception),
        Host = SafeHost(),
        OsDescription = SafeOsDescription(),
        UptimeSeconds = SafeUptimeSeconds(),
        RuntimeVersion = SafeRuntimeVersion(),
        AppVersion = SafeAppVersion(),
        WorkingSetBytes = SafeWorkingSet(),
        TotalManagedMemoryBytes = SafeManagedMemory(),
        Gen0Collections = SafeGc(0),
        Gen1Collections = SafeGc(1),
        Gen2Collections = SafeGc(2),
        ExitCode = exitCode,
        RecentLogTail = SafeTail()
    };

    private void WriteToFile(CrashReport report)
    {
        try
        {
            var dir = _options.CrashFilePath;
            if (string.IsNullOrWhiteSpace(dir))
            {
                return;
            }

            var line = JsonSerializer.Serialize(report, JsonOptions) + "\n";
            lock (_fileGate)
            {
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"{report.ProcessName}.crash.jsonl");
                RotateIfNeeded(dir, report.ProcessName, path);
                File.AppendAllText(path, line);
            }
        }
        catch (Exception ex)
        {
            // The file write failing must not stop the Mongo write that follows.
            ReportError($"[CrashReporter] failed to write crash file: {ex}");
        }
    }

    private void RotateIfNeeded(string dir, string processName, string path)
    {
        try
        {
            var max = _options.CrashFileMaxBytes;
            if (max <= 0)
            {
                return;
            }

            var info = new FileInfo(path);
            if (!info.Exists || info.Length < max)
            {
                return;
            }

            // Keep one rolled generation: {Process}.crash.1.jsonl (overwritten).
            var rolled = Path.Combine(dir, $"{processName}.crash.1.jsonl");
            if (File.Exists(rolled))
            {
                File.Delete(rolled);
            }

            File.Move(path, rolled);
        }
        catch (Exception ex)
        {
            ReportError($"[CrashReporter] failed to rotate crash file: {ex}");
        }
    }

    private void WriteToMongo(CrashReport report)
    {
        if (!context.IsActive)
        {
            return;
        }

        try
        {
            var timeout = _options.CrashMongoWriteTimeout > TimeSpan.Zero
                ? _options.CrashMongoWriteTimeout
                : TimeSpan.FromSeconds(3);

            using var cts = new CancellationTokenSource(timeout);
            // Synchronous, single-document insert (lossless, like MongoAuditLog). The
            // token bounds it so a Mongo outage times out instead of hanging exit; we
            // already have the durable file copy by this point.
            context.Crashes.InsertOneAsync(ToDocument(report), options: null, cts.Token)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            ReportError($"[CrashReporter] failed to persist crash to Mongo: {ex}");
        }
    }

    private static CrashReportDocument ToDocument(CrashReport report) => new()
    {
        ReportId = report.Id.ToString(),
        TimestampUtc = report.TimestampUtc,
        ProcessName = report.ProcessName,
        Source = report.Source.ToString(),
        ExceptionType = report.ExceptionType,
        Message = report.Message,
        StackTrace = report.StackTrace,
        InnerExceptions = report.InnerExceptions.Count == 0
            ? null
            : report.InnerExceptions
                .Select(e => new CrashExceptionDocument { Type = e.Type, Message = e.Message, StackTrace = e.StackTrace })
                .ToList(),
        Host = report.Host,
        OsDescription = report.OsDescription,
        UptimeSeconds = report.UptimeSeconds,
        RuntimeVersion = report.RuntimeVersion,
        AppVersion = report.AppVersion,
        WorkingSetBytes = report.WorkingSetBytes,
        TotalManagedMemoryBytes = report.TotalManagedMemoryBytes,
        Gen0Collections = report.Gen0Collections,
        Gen1Collections = report.Gen1Collections,
        Gen2Collections = report.Gen2Collections,
        ExitCode = report.ExitCode,
        RecentLogTail = report.RecentLogTail.Count == 0
            ? null
            : report.RecentLogTail
                .Select(e => new CrashLogTailDocument
                {
                    TimestampUtc = e.TimestampUtc,
                    Level = e.Level,
                    Category = e.Category,
                    Message = e.Message,
                    Exception = e.Exception
                })
                .ToList()
    };

    private static IReadOnlyList<CrashExceptionInfo> FlattenInner(Exception? top)
    {
        var result = new List<CrashExceptionInfo>();
        if (top is null)
        {
            return result;
        }

        void Walk(Exception ex, int depth)
        {
            if (depth > 20)
            {
                // Guard against pathological self-referential chains.
                return;
            }

            if (ex is AggregateException aggregate)
            {
                foreach (var child in aggregate.InnerExceptions)
                {
                    result.Add(Describe(child));
                    Walk(child, depth + 1);
                }
            }
            else if (ex.InnerException is not null)
            {
                result.Add(Describe(ex.InnerException));
                Walk(ex.InnerException, depth + 1);
            }
        }

        Walk(top, 0);
        return result;
    }

    private static CrashExceptionInfo Describe(Exception ex)
        => new(ex.GetType().FullName ?? ex.GetType().Name, ex.Message, ex.StackTrace);

    private IReadOnlyList<CrashLogTailEntry> SafeTail()
    {
        try
        {
            return tail.Snapshot();
        }
        catch
        {
            return [];
        }
    }

    private static string? SafeHost()
    {
        try
        {
            return Environment.MachineName;
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeOsDescription()
    {
        try
        {
            return RuntimeInformation.OSDescription;
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeRuntimeVersion()
    {
        try
        {
            return RuntimeInformation.FrameworkDescription;
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeAppVersion()
    {
        try
        {
            var entry = Assembly.GetEntryAssembly();
            return entry?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? entry?.GetName().Version?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static long SafeWorkingSet()
    {
        try
        {
            return Environment.WorkingSet;
        }
        catch
        {
            return 0;
        }
    }

    private static long SafeManagedMemory()
    {
        try
        {
            return GC.GetTotalMemory(forceFullCollection: false);
        }
        catch
        {
            return 0;
        }
    }

    private static int SafeGc(int generation)
    {
        try
        {
            return GC.CollectionCount(generation);
        }
        catch
        {
            return 0;
        }
    }

    private static double SafeUptimeSeconds()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return Math.Max(0, (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalSeconds);
        }
        catch
        {
            return 0;
        }
    }

    private static string FormatMb(long bytes)
        => bytes <= 0 ? "n/a" : $"{bytes / (1024d * 1024d):N0} MB";

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
