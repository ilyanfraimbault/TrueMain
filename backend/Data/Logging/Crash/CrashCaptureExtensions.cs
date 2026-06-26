using Data.Logging.Mongo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Data.Logging.Crash;

/// <summary>
/// Registration + wiring helpers for durable crash capture, mirroring
/// <see cref="MongoLoggingServiceCollectionExtensions"/> so the API and the Ingestor
/// set it up identically. Call <see cref="AddCrashReporting"/> during service
/// registration (after <c>AddMongoLogging</c>, which it depends on for
/// <see cref="MongoLogContext"/> / <see cref="MongoLoggingOptions"/>) and
/// <see cref="UseProcessCrashCapture"/> once, right after the host is built.
/// </summary>
public static class CrashCaptureExtensions
{
    private static int _hooksRegistered;

    public static IServiceCollection AddCrashReporting(this IServiceCollection services)
    {
        // One ring-buffer provider instance, registered both as the concrete type
        // (so CrashReporter can Snapshot() it) and as a logging provider (so it
        // receives Information+ records to buffer). The factory descriptor is typed
        // <ILoggerProvider, RecentLogTailProvider> rather than just <ILoggerProvider>:
        // TryAddEnumerable dedupes on the implementation type, and an untyped factory
        // would make it indistinguishable from the other ILoggerProvider registrations
        // (and throw).
        services.TryAddSingleton<RecentLogTailProvider>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider, RecentLogTailProvider>(
                sp => sp.GetRequiredService<RecentLogTailProvider>()));

        services.TryAddSingleton<ICrashReporter, CrashReporter>();

        // The read path for the admin Crashes panel. Reuses the singleton
        // MongoLogContext, registered in both hosts like IMongoLogQuery.
        services.TryAddSingleton<ICrashQuery, CrashQuery>();

        // Keeps the sentinel's memory snapshot fresh for OOM diagnosis.
        services.AddHostedService<CrashSentinelRefreshService>();

        return services;
    }

    /// <summary>
    /// Wires the process-level exception hooks, detects an unclean death of the
    /// previous run, and marks this run's sentinel. Call once right after
    /// <c>builder.Build()</c>, before <c>Run()</c>, so a startup or background-thread
    /// fault is still captured.
    /// </summary>
    public static void UseProcessCrashCapture(this IServiceProvider services)
    {
        // Wire the process-global hooks + sentinel exactly once per process:
        // integration tests build many hosts in one AppDomain, and re-registering
        // would accumulate stale handlers bound to disposed DI containers.
        if (Interlocked.CompareExchange(ref _hooksRegistered, 1, 0) != 0)
        {
            return;
        }

        var options = services.GetRequiredService<IOptions<MongoLoggingOptions>>().Value;
        var reporter = services.GetRequiredService<ICrashReporter>();
        var processName = CrashSentinel.ResolveProcessName(options);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            reporter.Report(CrashSource.AppDomainUnhandled, e.ExceptionObject as Exception
                ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown non-CLR exception"));

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            reporter.Report(CrashSource.TaskSchedulerUnobserved, e.Exception);
            e.SetObserved();
        };

        // A previous run that died uncleanly left its sentinel at "running".
        var previous = CrashSentinel.ReadPrevious(options, processName);
        if (previous is not null
            && string.Equals(previous.Status, CrashSentinel.StatusRunning, StringComparison.Ordinal))
        {
            reporter.ReportUncleanShutdown(previous);
        }

        CrashSentinel.MarkRunning(options, processName);

        var lifetime = services.GetService<IHostApplicationLifetime>();
        lifetime?.ApplicationStopping.Register(() => CrashSentinel.MarkStopped(options, processName));
    }
}
