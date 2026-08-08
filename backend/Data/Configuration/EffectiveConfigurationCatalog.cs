namespace Data.Configuration;

/// <summary>
/// The allow-list of configuration sections one host exposes (#1034), declared next to the
/// options classes it names.
///
/// <para>
/// This is an allow-list, not a filter. <see cref="EffectiveConfigurationBuilder"/> walks
/// <see cref="Sections"/> and never enumerates configuration: nothing is dumped and then
/// redacted. A deny-list would be one forgotten property away from returning the Riot key,
/// and the property that leaks is always the one added after the filter was written.
/// </para>
/// </summary>
/// <param name="ProcessName">The host these sections belong to — <c>Api</c> or <c>Ingestor</c>.</param>
/// <param name="Sections">The exposed sections, in presentation order.</param>
public sealed record EffectiveConfigurationCatalog(
    string ProcessName,
    IReadOnlyList<EffectiveConfigurationSectionDescriptor> Sections);

/// <summary>One exposed section: which options class to read, and how to describe it.</summary>
public sealed record EffectiveConfigurationSectionDescriptor
{
    /// <summary>The configuration key prefix, e.g. <c>StorageHistory</c>. Must match the bound section.</summary>
    public required string SectionName { get; init; }

    /// <summary>
    /// The options class. Read through <c>IOptions&lt;T&gt;</c> from the host's own container,
    /// so the values are the ones the process bound and validated at boot.
    /// </summary>
    public required Type OptionsType { get; init; }

    /// <summary>Short display title.</summary>
    public required string Title { get; init; }

    /// <summary>One sentence saying what this section controls.</summary>
    public required string Description { get; init; }

    /// <summary>
    /// When null, every public readable instance property is rendered. When set, those
    /// properties and only those — required for any type that also carries a secret, which
    /// today means <c>MongoLogging</c> (its retention windows are worth showing; its
    /// connection string must never be). A guard test enforces that requirement.
    /// </summary>
    public IReadOnlyList<string>? IncludeProperties { get; init; }

    /// <summary>
    /// Values whose absence has a consequence elsewhere in the portal, so the page can name
    /// it instead of printing a bare zero.
    /// </summary>
    public IReadOnlyList<EffectiveConfigurationNotice> Notices { get; init; } = [];
}

/// <summary>
/// "This option is unset, and here is what that means." Attached to a rendered value only
/// while <see cref="When"/> holds, so setting the option makes the notice disappear.
/// </summary>
/// <param name="PropertyName">The property the notice watches.</param>
/// <param name="When">The state that triggers it.</param>
/// <param name="Consequence">
/// One sentence, operator-facing: what is degraded, and what to set to fix it.
/// </param>
public sealed record EffectiveConfigurationNotice(
    string PropertyName,
    UnsetCondition When,
    string Consequence);

/// <summary>The states an <see cref="EffectiveConfigurationNotice"/> can watch for.</summary>
public enum UnsetCondition
{
    /// <summary>A numeric or <c>TimeSpan</c> value at or below zero — the codebase's "disabled" sentinel.</summary>
    ZeroOrNegative,

    /// <summary>A null, empty or whitespace string.</summary>
    EmptyText,

    /// <summary>An empty collection.</summary>
    EmptyList,

    /// <summary>A null value of any type.</summary>
    Null
}
