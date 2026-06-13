using Data.Entities;

namespace TrueMain.Services.Ops;

/// <summary>
/// Shared read-side policy for ageing a stale in-flight run out to
/// <see cref="ProcessRunStatus.Abandoned"/>. Both the runs and iterations query
/// services apply this so "what's running now" stays consistent across views and
/// the threshold lives in exactly one place.
/// </summary>
internal static class ProcessRunStaleness
{
    /// <summary>
    /// A Running row whose heartbeat (refreshed every 30s) is older than this — or
    /// missing entirely — is treated as Abandoned: its owner died without
    /// finalising it. Four missed beats keeps a healthy-but-slow run from flapping.
    /// </summary>
    public static readonly TimeSpan Threshold = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Maps a run's stored status to its effective status: a Running row reads as
    /// Abandoned once its heartbeat is missing or older than <see cref="Threshold"/>;
    /// every other status passes through unchanged.
    /// </summary>
    public static ProcessRunStatus EffectiveStatus(ProcessRunStatus status, DateTime? lastHeartbeatAtUtc, DateTime now)
    {
        if (status != ProcessRunStatus.Running)
        {
            return status;
        }

        var stale = lastHeartbeatAtUtc is null || lastHeartbeatAtUtc < now - Threshold;
        return stale ? ProcessRunStatus.Abandoned : ProcessRunStatus.Running;
    }
}
