namespace TrueMain.ReadModels.Ops;

/// <summary>
/// The <c>/health</c> page's alerts panel (#2292): whether the alerter is on, the rules it
/// applies, and the last alerts it sent.
/// </summary>
public sealed record HealthAlertsReadModel
{
    /// <summary>False when no channel is configured: the alerter is off and <see cref="Alerts"/> only holds history.</summary>
    public bool Enabled { get; init; }

    /// <summary>The channel alerts go to (<c>discord</c>), or null when off.</summary>
    public string? Channel { get; init; }

    public int EvaluationIntervalSeconds { get; init; }

    /// <summary>Consecutive red evaluations before a signal counts as failing.</summary>
    public int RedConfirmations { get; init; }

    public int CooldownMinutes { get; init; }

    /// <summary>Consecutive failed runs at which a process is announced.</summary>
    public int ProcessFailureThreshold { get; init; }

    /// <summary>When the alerter last evaluated; null when it never has (or Mongo is not configured).</summary>
    public DateTime? LastEvaluatedAtUtc { get; init; }

    /// <summary>Keys of the signals and processes with an incident open right now.</summary>
    public IReadOnlyList<string> OpenIncidents { get; init; } = [];

    /// <summary>The newest alerts first.</summary>
    public IReadOnlyList<HealthAlertReadModel> Alerts { get; init; } = [];
}

public sealed record HealthAlertReadModel
{
    public string Id { get; init; } = string.Empty;

    public DateTime SentAtUtc { get; init; }

    /// <summary><c>failing</c> | <c>recovered</c>.</summary>
    public string Kind { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Verdict { get; init; } = string.Empty;

    public string Channel { get; init; } = string.Empty;

    public bool Delivered { get; init; }

    public string? Error { get; init; }

    public IReadOnlyList<HealthAlertChangeReadModel> Changes { get; init; } = [];
}

public sealed record HealthAlertChangeReadModel
{
    public string Key { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    /// <summary><c>failing</c> | <c>recovered</c>.</summary>
    public string Change { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string Headline { get; init; } = string.Empty;
}
