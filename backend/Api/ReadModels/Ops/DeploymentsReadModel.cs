namespace TrueMain.ReadModels.Ops;

/// <summary>
/// The version changes of every backend process over a window (#2297), drawn by the admin
/// time-series charts as deploy markers. A restart on the same version is not in it.
/// </summary>
public sealed record DeploymentsReadModel
{
    public DateTime FromUtc { get; init; }

    public DateTime ToUtc { get; init; }

    /// <summary>Every version change in the window, oldest first.</summary>
    public IReadOnlyList<DeploymentReadModel> Deployments { get; init; } = [];
}

/// <summary>One process booting a new version.</summary>
public sealed record DeploymentReadModel
{
    /// <summary><c>Api</c> or <c>Ingestor</c>.</summary>
    public string ProcessName { get; init; } = string.Empty;

    /// <summary>Which container of the process (the ingestor's job mode); null for the Api.</summary>
    public string? Role { get; init; }

    public string Version { get; init; } = string.Empty;

    /// <summary>The version it replaced; null on the process's first recorded boot.</summary>
    public string? PreviousVersion { get; init; }

    /// <summary>When the process booted on <see cref="Version"/>: the rollout time.</summary>
    public DateTime DeployedAtUtc { get; init; }
}
