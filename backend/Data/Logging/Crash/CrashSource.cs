namespace Data.Logging.Crash;

/// <summary>
/// What triggered a <see cref="CrashReport"/>. Persisted by its name (parity with
/// how <c>LogLevel</c> / <c>ProcessRunStatus</c> are stored), so the admin Crashes
/// panel can filter on an indexable <c>$eq</c> over the source name.
/// </summary>
public enum CrashSource
{
    /// <summary>
    /// An exception reached <see cref="System.AppDomain.UnhandledException"/> — an
    /// unhandled throw on any thread (including background tasks). The process is
    /// terminating; we record before it dies. Carries a full stack trace.
    /// </summary>
    AppDomainUnhandled,

    /// <summary>
    /// A faulted <see cref="System.Threading.Tasks.Task"/> was never observed and
    /// surfaced via <see cref="System.Threading.Tasks.TaskScheduler.UnobservedTaskException"/>.
    /// Non-terminal: the process keeps running (we call <c>SetObserved</c>), but the
    /// fault is recorded because it often hides the real cause of later trouble.
    /// </summary>
    TaskSchedulerUnobserved,

    /// <summary>
    /// An exception propagated out of the host's run loop (<c>app.Run()</c> /
    /// <c>host.RunAsync()</c>) and was caught by the top-level try/catch in
    /// <c>Program</c>. Covers startup faults (options validation, migrations) and
    /// faults that escape the background worker. Carries a full stack trace.
    /// </summary>
    HostRun,

    /// <summary>
    /// Detected at the next startup: the previous run's sentinel still said
    /// "running", meaning the process vanished without a graceful shutdown. This is
    /// the signature of an uncatchable death — an OOM kill / SIGKILL (exit 137), a
    /// <see cref="System.StackOverflowException"/> or <c>Environment.FailFast</c>.
    /// No stack trace is possible; the report carries the dead run's last-known
    /// memory/GC snapshot instead, which is the strongest available OOM signal.
    /// </summary>
    UncleanShutdown
}
