namespace TrueMain.ReadModels.Ops;

/// <summary>
/// What every host is actually running with (#1034): one entry per process, each grouped
/// into the allow-listed sections <see cref="Data.Configuration.EffectiveConfigurationCatalog"/>
/// declares for it.
/// </summary>
public sealed record EffectiveConfigurationOverviewReadModel
{
    /// <summary>One entry per process, sorted by process name.</summary>
    public IReadOnlyList<EffectiveConfigurationProcessReadModel> Processes { get; init; } = [];
}

/// <summary>One process's snapshot: which build, which environment, and its sections.</summary>
public sealed record EffectiveConfigurationProcessReadModel
{
    /// <summary>Which host bound these values — <c>Api</c> or <c>Ingestor</c>.</summary>
    public string ProcessName { get; init; } = string.Empty;

    /// <summary>The host environment name (<c>Production</c>, <c>Development</c>, …).</summary>
    public string Environment { get; init; } = string.Empty;

    /// <summary>The build this process is running, or null for a plain local build.</summary>
    public string? Version { get; init; }

    /// <summary>
    /// When this snapshot was taken. For the Api this is always "now" — it is built live
    /// on every request. For the Ingestor it is the boot time of its last run: still what
    /// that process is running, even if older than the last deploy.
    /// </summary>
    public DateTime CapturedAtUtc { get; init; }

    /// <summary>The allow-listed sections, in catalog order.</summary>
    public IReadOnlyList<EffectiveConfigurationSectionReadModel> Sections { get; init; } = [];
}

/// <summary>One configuration section's worth of values, with the prose explaining what it drives.</summary>
public sealed record EffectiveConfigurationSectionReadModel
{
    /// <summary>The configuration key prefix, e.g. <c>StorageHistory</c>.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Short display title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>One sentence saying what this section controls.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The rendered values, in the order the properties are declared on the options class.</summary>
    public IReadOnlyList<EffectiveConfigurationValueReadModel> Values { get; init; } = [];
}

/// <summary>A single bound option, as the process holds it.</summary>
public sealed record EffectiveConfigurationValueReadModel
{
    /// <summary>Fully-qualified configuration key, e.g. <c>StorageHistory:DiskCapacityBytes</c>.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>The property name alone, e.g. <c>DiskCapacityBytes</c>.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The pasteable-back-into-configuration form. Null when the option is unset.</summary>
    public string? Value { get; init; }

    /// <summary>The humanised form ("90 days", "1.0 TB"), or null when it would repeat <see cref="Value"/>.</summary>
    public string? ValueLabel { get; init; }

    /// <summary><c>default</c>, <c>override</c> or <c>derived</c> — see <see cref="Data.Configuration.EffectiveConfigurationOrigins"/>.</summary>
    public string Origin { get; init; } = string.Empty;

    /// <summary>Which provider supplied an override, e.g. <c>environment</c>. Null for <c>default</c>/<c>derived</c>.</summary>
    public string? Source { get; init; }

    /// <summary>How to read the number — see <see cref="Data.Configuration.EffectiveConfigurationUnits"/>.</summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary>Set when the value is unset and that has a visible consequence elsewhere in the portal.</summary>
    public string? Notice { get; init; }
}
