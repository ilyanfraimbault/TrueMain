namespace Data.Logging.Crash;

/// <summary>
/// Records a process crash durably to a local file (first, so it survives Mongo
/// being down) and to the Mongo <c>crashes</c> collection (second, time-bounded so a
/// Mongo outage can never block process exit).
/// </summary>
/// <remarks>
/// <see cref="Report"/> is <b>synchronous and blocking by design</b>: it is called
/// from <see cref="System.AppDomain.UnhandledException"/>, where the process is about
/// to terminate and there is no opportunity to <c>await</c>. This is the one place in
/// the codebase where blocking on async is correct.
/// </remarks>
public interface ICrashReporter
{
    /// <summary>True when the reporter will attempt to record (the file sink is always available).</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Records a crash from the given <paramref name="source"/>. Never throws — every
    /// failure is swallowed and reported to <see cref="System.Console.Error"/>.
    /// </summary>
    void Report(CrashSource source, Exception? exception, int? exitCode = null);

    /// <summary>
    /// Records an <see cref="CrashSource.UncleanShutdown"/> for a previous run that
    /// vanished without a graceful shutdown (OOM/SIGKILL/hard crash), using the dead
    /// run's last-known memory/GC snapshot from its sentinel.
    /// </summary>
    void ReportUncleanShutdown(CrashSentinelState previous);
}
