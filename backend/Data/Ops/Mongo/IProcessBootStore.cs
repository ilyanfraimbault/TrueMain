namespace Data.Ops.Mongo;

/// <summary>A boot as a process reports it (#2297), before the store classifies it.</summary>
public sealed record ProcessBoot(
    string ProcessName,
    string? Role,
    string Environment,
    string? Version,
    DateTime StartedAtUtc);

/// <summary>A boot that changed its process's version: a deploy (#2297).</summary>
public sealed record ProcessDeployment(
    string ProcessName,
    string? Role,
    string Version,
    string? PreviousVersion,
    DateTime DeployedAtUtc);

/// <summary>What <see cref="IProcessBootStore.GetDeploymentsAsync"/> read.</summary>
/// <param name="Before">Per process and role, its last version change before the window.</param>
/// <param name="InWindow">Every version change inside the window, oldest first.</param>
public sealed record ProcessDeploymentWindow(
    IReadOnlyList<ProcessDeployment> Before,
    IReadOnlyList<ProcessDeployment> InWindow);

/// <summary>
/// The boot log behind the admin charts' deploy markers (#2297): every Api and Ingestor appends
/// its boot, the Api reads the version changes back.
/// </summary>
public interface IProcessBootStore
{
    /// <summary>
    /// Appends <paramref name="boot"/>, classified against the process's previous versioned
    /// boot. Returns null when Mongo is not configured, true when the boot changed the version
    /// (a deploy), false for a restart.
    /// </summary>
    Task<bool?> RecordAsync(ProcessBoot boot, CancellationToken ct);

    /// <summary>
    /// The version changes recorded in <c>[fromUtc, toUtc]</c>, plus — per process and role —
    /// the last one before <paramref name="fromUtc"/>, which is what the first change in the
    /// window is compared to. Empty when Mongo is not configured.
    /// </summary>
    Task<ProcessDeploymentWindow> GetDeploymentsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct);
}
