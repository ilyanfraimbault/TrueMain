using System.Globalization;
using Data.Logging.Crash;

namespace TrueMain.Services.Ops;

/// <summary>
/// Derives a plain-language explanation for a crash report (#722): what kind of
/// death the source implies, refined by the exception chain and — for unclean
/// shutdowns — the dead run's memory snapshot. Purely heuristic display text for
/// the admin Crashes panel; it never replaces the raw report, it fronts it.
/// </summary>
public static class CrashExplainer
{
    /// <summary>
    /// Working-set floor above which an unclean shutdown reads as a probable
    /// OOM kill. Conservative: the prod containers run in the low hundreds of
    /// MB, and every observed OOM death (#600) was well past this mark.
    /// </summary>
    private const long OomWorkingSetBytes = 2L * 1024 * 1024 * 1024;

    public static string Explain(CrashRow row)
    {
        var baseText = BaseText(row);
        var causeHint = CauseHint(row);
        return causeHint is null ? baseText : $"{baseText} {causeHint}";
    }

    private static string BaseText(CrashRow row)
    {
        if (!Enum.TryParse<CrashSource>(row.Source, ignoreCase: true, out var source))
        {
            return "Recorded process crash.";
        }

        return source switch
        {
            CrashSource.UncleanShutdown => ExplainUncleanShutdown(row),
            CrashSource.AppDomainUnhandled =>
                "An unhandled exception (typically on a background thread) terminated the process.",
            CrashSource.TaskSchedulerUnobserved =>
                "A faulted task was never awaited or observed. The process kept running — "
                + "this is a latent fault, often an early symptom of trouble that surfaces later.",
            CrashSource.HostRun =>
                "A fatal exception escaped the host's run loop — typically a startup failure "
                + "(configuration, options validation, database migration) or an error that "
                + "broke the main worker.",
            _ => "Recorded process crash."
        };
    }

    private static string ExplainUncleanShutdown(CrashRow row)
    {
        var text = "The process vanished without a graceful shutdown — killed by the OS or a "
                   + "fatal runtime halt, so no exception could be captured.";

        if (row.ExitCode is 137)
        {
            // 128 + SIGKILL(9): the container runtime's OOM-kill signature.
            return $"{text} Exit code 137 (SIGKILL) — almost certainly an out-of-memory kill.";
        }

        if (row.WorkingSetBytes >= OomWorkingSetBytes)
        {
            var gigabytes = (row.WorkingSetBytes / (1024.0 * 1024 * 1024))
                .ToString("0.0", CultureInfo.InvariantCulture);
            return $"{text} The last-known working set was {gigabytes} GB — consistent with an out-of-memory kill.";
        }

        if (row.UptimeSeconds > 0 && row.UptimeSeconds < 60)
        {
            return $"{text} The process had been up for under a minute — possibly a crash loop.";
        }

        return text;
    }

    /// <summary>
    /// A refinement drawn from the exception chain: names the failing subsystem
    /// when the exception types make it recognizable. Null when there is nothing
    /// beyond the base text to add.
    /// </summary>
    private static string? CauseHint(CrashRow row)
    {
        var typeNames = new List<string>();
        if (!string.IsNullOrWhiteSpace(row.ExceptionType))
        {
            typeNames.Add(row.ExceptionType);
        }

        typeNames.AddRange(row.InnerExceptions.Select(inner => inner.Type));

        foreach (var typeName in typeNames)
        {
            if (typeName.Contains("OutOfMemory", StringComparison.OrdinalIgnoreCase))
            {
                return "The exception chain points at memory exhaustion.";
            }

            if (typeName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
                || typeName.Contains("Postgres", StringComparison.OrdinalIgnoreCase))
            {
                return "The exception chain points at PostgreSQL (connectivity, timeout or a failing query/migration).";
            }

            if (typeName.Contains("MongoDB", StringComparison.OrdinalIgnoreCase)
                || typeName.StartsWith("MongoDB.", StringComparison.Ordinal)
                || typeName.Contains(".Mongo", StringComparison.OrdinalIgnoreCase))
            {
                return "The exception chain points at MongoDB (connectivity or timeout).";
            }

            if (typeName.Contains("HttpRequestException", StringComparison.OrdinalIgnoreCase)
                || typeName.Contains("SocketException", StringComparison.OrdinalIgnoreCase))
            {
                return "The exception chain points at an outbound network failure (e.g. the Riot API being unreachable).";
            }

            if (typeName.Contains("OptionsValidationException", StringComparison.OrdinalIgnoreCase))
            {
                return "The exception chain points at invalid configuration (options validation failed at startup).";
            }

            if (typeName.Contains("TimeoutException", StringComparison.OrdinalIgnoreCase))
            {
                return "The exception chain points at an operation exceeding its timeout.";
            }
        }

        return null;
    }
}
