namespace Data.Configuration;

/// <summary>
/// What one process is actually running with: the options it bound at boot, section by
/// section, each value tagged with where it came from (#1034).
///
/// <para>
/// This is a snapshot of <c>IOptions</c> values, never a re-parse of a settings file. The
/// distinction is the whole point of the feature: the API and the Ingestor run in separate
/// containers with different environments, so reading the ingestor's <c>appsettings.json</c>
/// from the API would report defaults for the four knobs production actually overrides.
/// </para>
/// </summary>
public sealed record EffectiveConfigurationSnapshot
{
    /// <summary>Which host bound these values — <c>Api</c> or <c>Ingestor</c>.</summary>
    public string ProcessName { get; init; } = string.Empty;

    /// <summary>The host environment name (<c>Production</c>, <c>Development</c>, …).</summary>
    public string Environment { get; init; } = string.Empty;

    /// <summary>
    /// The build the process is running, from the assembly's informational version. Null
    /// when the assembly carries none, which is the case for a plain local build.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// When the snapshot was taken. For a stored snapshot this is the publishing process's
    /// boot time, and the page states its age: a value published before the last deploy is
    /// still the one that process is running, but it may not be the one the compose file says.
    /// </summary>
    public DateTime CapturedAtUtc { get; init; }

    /// <summary>The allow-listed sections, in catalog order.</summary>
    public IReadOnlyList<EffectiveConfigurationSection> Sections { get; init; } = [];
}

/// <summary>One configuration section's worth of values, with the prose explaining what it drives.</summary>
public sealed record EffectiveConfigurationSection
{
    /// <summary>The configuration key prefix, e.g. <c>StorageHistory</c>.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Short display title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>One sentence saying what this section controls, for an operator who did not write it.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The rendered values, in the order the properties are declared on the options class.</summary>
    public IReadOnlyList<EffectiveConfigurationValue> Values { get; init; } = [];
}

/// <summary>A single bound option, as the process holds it.</summary>
public sealed record EffectiveConfigurationValue
{
    /// <summary>Fully-qualified configuration key, e.g. <c>StorageHistory:DiskCapacityBytes</c>.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>The property name alone, e.g. <c>DiskCapacityBytes</c>.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The value in a form that could be pasted back into configuration (invariant culture,
    /// no thousands separators, <c>TimeSpan</c> in its round-trip form). Null when the option
    /// itself is null — the page prints "not set" rather than an empty cell.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>
    /// The same value humanised for reading ("90 days", "1.0 TB"). Null when it would just
    /// repeat <see cref="Value"/>.
    /// </summary>
    public string? ValueLabel { get; init; }

    /// <summary>
    /// <c>default</c> (no provider supplies the key and the value matches the class default),
    /// <c>override</c> (a configuration provider supplies it) or <c>derived</c> (no provider
    /// supplies it, yet the value differs from the class default — something post-configured
    /// it at boot). See <see cref="EffectiveConfigurationOrigins"/>.
    /// </summary>
    public string Origin { get; init; } = EffectiveConfigurationOrigins.Default;

    /// <summary>
    /// Which provider supplied an override — <c>environment</c>, <c>appsettings.json</c>,
    /// <c>appsettings.Production.json</c>, … Null for <c>default</c> and <c>derived</c>.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>How to read the number. See <see cref="EffectiveConfigurationUnits"/>.</summary>
    public string Unit { get; init; } = EffectiveConfigurationUnits.Text;

    /// <summary>
    /// Set when the value is unset (or disabled) <em>and</em> that has a consequence the
    /// operator can see elsewhere in the portal — the sentence naming that consequence. This
    /// is what keeps an absent disk capacity from rendering as a bare <c>0</c>.
    /// </summary>
    public string? Notice { get; init; }
}

/// <summary>The <see cref="EffectiveConfigurationValue.Origin"/> vocabulary.</summary>
public static class EffectiveConfigurationOrigins
{
    /// <summary>No provider supplies the key; the process runs the value compiled into the class.</summary>
    public const string Default = "default";

    /// <summary>A configuration provider supplies the key; <c>Source</c> names which one.</summary>
    public const string Override = "override";

    /// <summary>
    /// No provider supplies the key, yet the bound value differs from the class default —
    /// something computed it at boot. The ingestor's per-stage <c>Platforms</c> lists, filled
    /// in from the shared <c>Platforms:Active</c> scope, are the live example: calling those
    /// "default" would be a lie.
    /// </summary>
    public const string Derived = "derived";
}

/// <summary>The <see cref="EffectiveConfigurationValue.Unit"/> vocabulary.</summary>
public static class EffectiveConfigurationUnits
{
    /// <summary>A byte count.</summary>
    public const string Bytes = "bytes";

    /// <summary>A duration — a <c>TimeSpan</c>, or a number whose name ends in a time unit.</summary>
    public const string Duration = "duration";

    /// <summary>A plain count, batch size or weight.</summary>
    public const string Count = "count";

    /// <summary>A percentage.</summary>
    public const string Percent = "percent";

    /// <summary>A boolean switch.</summary>
    public const string Flag = "flag";

    /// <summary>A list of values.</summary>
    public const string List = "list";

    /// <summary>Anything else — free text, an enum name.</summary>
    public const string Text = "text";
}
