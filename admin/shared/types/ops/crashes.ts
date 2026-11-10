// Crash reports — `GET /api/ops/crashes`.

/**
 * What triggered a crash report. `UncleanShutdown` is detected at the next boot
 * (the previous run vanished with no graceful stop — OOM/SIGKILL/hard crash) and
 * carries no stack trace, just the dead run's last-known memory snapshot.
 */
export type CrashSource
  = | 'AppDomainUnhandled'
    | 'TaskSchedulerUnobserved'
    | 'HostRun'
    | 'UncleanShutdown'
