using Data.Ops.Mongo;

namespace TrueMain.Services.Ops.Processes;

/// <summary>
/// Reduces the recorded version changes to the deploys (#2297). The store already flags a boot
/// as a change against its process's previous versioned boot; what is left here is the case that
/// flag cannot see — two containers of one process (same role) booting the same release at
/// once both read the old version and both flag themselves. Kept pure so it is tested apart
/// from Mongo.
/// </summary>
public static class DeploymentTimeline
{
    /// <summary>
    /// The deploys in <paramref name="inWindow"/>, oldest first: per process and role, a change
    /// is kept only when its version differs from the last kept one — seeded with that process's
    /// last change before the window, from <paramref name="before"/>.
    /// </summary>
    public static IReadOnlyList<ProcessDeployment> Collapse(
        IReadOnlyList<ProcessDeployment> before,
        IReadOnlyList<ProcessDeployment> inWindow)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(inWindow);

        var lastVersion = new Dictionary<(string ProcessName, string? Role), string>();
        foreach (var seed in before)
        {
            var key = (seed.ProcessName, seed.Role);
            if (!lastVersion.TryGetValue(key, out _))
            {
                lastVersion[key] = seed.Version;
            }
        }

        var kept = new List<ProcessDeployment>(inWindow.Count);
        foreach (var change in inWindow.OrderBy(change => change.DeployedAtUtc))
        {
            var key = (change.ProcessName, change.Role);
            lastVersion.TryGetValue(key, out var previous);
            if (!ProcessVersions.IsVersionChange(previous, change.Version))
            {
                continue;
            }

            lastVersion[key] = change.Version;
            kept.Add(change);
        }

        return kept;
    }
}
