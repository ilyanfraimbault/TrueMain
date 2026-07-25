using Ingestor.Processes.Common;

namespace Ingestor.Options;

/// <summary>
/// Single source of truth for the platforms the pipeline is active on (#496).
/// <para>
/// Discovery, MatchIngestion and Harvest each expose their own <c>Platforms</c> list. They used
/// to be three independent copies of the same regions, so adding a region to one section and
/// forgetting another silently skipped it — e.g. a platform discovered and ingested but never
/// harvested, with no startup error. Those per-section lists are now optional narrowing
/// overrides: left empty (the default) they inherit <see cref="Active"/>, so a region is added
/// in exactly one place, and any override is validated against this list at startup by
/// <see cref="PlatformScopeValidator"/>.
/// </para>
/// </summary>
public class PlatformScopeOptions
{
    public const string SectionName = "Platforms";

    /// <summary>
    /// Platform identifiers the pipeline runs on (e.g. <c>KR</c>, <c>EUW1</c>). Must be non-empty
    /// and contain only known Riot platform ids; every section's effective platform list must be
    /// a subset of it.
    /// <para>
    /// Deliberately empty by default, with the shipped value living in <c>appsettings.json</c>:
    /// <see cref="Microsoft.Extensions.Configuration.ConfigurationBinder"/> <em>appends</em> bound
    /// entries to a list that already has items, so a hard-coded default would survive — and be
    /// silently unioned into — any narrower list an operator configures. An empty list fails
    /// startup validation with an explicit message instead.
    /// </para>
    /// </summary>
    public List<string> Active { get; set; } = [];

    /// <summary>
    /// Resolves the effective platform list of a section: its own override when it declares one,
    /// otherwise the shared <see cref="Active"/> list. Both sides are normalized
    /// (trimmed / upper-cased / deduplicated), so a section configured with blanks only is
    /// treated as "no override" rather than as "no platforms".
    /// </summary>
    /// <param name="sectionPlatforms">The section's configured platform list.</param>
    /// <returns>The normalized platforms the section should run on.</returns>
    public List<string> Resolve(IEnumerable<string> sectionPlatforms)
    {
        var overrides = PlatformNormalizer.Normalize(sectionPlatforms);
        return overrides.Count > 0 ? overrides : PlatformNormalizer.Normalize(Active);
    }
}
