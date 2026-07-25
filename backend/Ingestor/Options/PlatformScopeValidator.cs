using Core.Lol.Identifiers;
using Ingestor.Processes.Common;
using Microsoft.Extensions.Options;

namespace Ingestor.Options;

/// <summary>
/// Startup validation of the platform lists (#496): the one place that owns "the per-section
/// platform lists must agree". Runs through <c>ValidateOnStart</c>, so a divergent configuration
/// fails the boot instead of silently skipping a region for one pipeline stage.
/// <para>Invariants:</para>
/// <list type="bullet">
/// <item><c>Platforms:Active</c> is non-empty and holds only known Riot platform ids — an unknown
/// id (e.g. <c>EUW</c> instead of <c>EUW1</c>) is skipped at runtime with nothing but a warning,
/// which is exactly the kind of silent divergence this guards against.</item>
/// <item>Each section's effective list is a subset of <c>Platforms:Active</c>: a section can
/// narrow the scope explicitly, but a region is only ever <em>added</em> to the shared list, from
/// where every non-overriding section inherits it.</item>
/// <item><c>Harvest:Platforms</c> is a subset of <c>MatchIngestion:Platforms</c>: the harvest
/// mines <c>match_participants</c> rows, so harvesting a platform we never ingest is a no-op.</item>
/// </list>
/// </summary>
/// <param name="platformScope">
/// The shared scope, bound from configuration by <see cref="OptionsConfigurationExtensions"/>.
/// </param>
/// <param name="matchIngestionPlatforms">
/// <c>MatchIngestion:Platforms</c> as configured, for the harvest subset check. Passed in as plain
/// data rather than injected as <c>IOptions&lt;MatchIngestionOptions&gt;</c>: a validator of an
/// option type cannot depend on that same option type, because building it needs the very options
/// factory that is waiting on this validator.
/// </param>
internal sealed class PlatformScopeValidator(
    PlatformScopeOptions platformScope,
    IEnumerable<string> matchIngestionPlatforms) :
    IValidateOptions<PlatformScopeOptions>,
    IValidateOptions<DiscoveryOptions>,
    IValidateOptions<MatchIngestionOptions>,
    IValidateOptions<HarvestOptions>
{
    private const string ActiveKey = $"{PlatformScopeOptions.SectionName}:Active";

    public ValidateOptionsResult Validate(string? name, PlatformScopeOptions options)
    {
        if (!IsDefaultInstance(name))
        {
            return ValidateOptionsResult.Skip;
        }

        var active = PlatformNormalizer.Normalize(options.Active);
        if (active.Count == 0)
        {
            return ValidateOptionsResult.Fail($"{ActiveKey} must contain at least one value.");
        }

        var unknown = active.Where(platform => !PlatformId.TryParse(platform, out _)).ToList();
        return unknown.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{ActiveKey} contains unknown platform id(s): {Join(unknown)}. "
                + "Expected Riot platform ids such as KR, EUW1, NA1.");
    }

    public ValidateOptionsResult Validate(string? name, DiscoveryOptions options)
    {
        return ValidateSectionScope(name, DiscoveryOptions.SectionName, options.Platforms);
    }

    public ValidateOptionsResult Validate(string? name, MatchIngestionOptions options)
    {
        return ValidateSectionScope(name, MatchIngestionOptions.SectionName, options.Platforms);
    }

    public ValidateOptionsResult Validate(string? name, HarvestOptions options)
    {
        var scopeResult = ValidateSectionScope(name, HarvestOptions.SectionName, options.Platforms);
        if (scopeResult.Skipped || scopeResult.Failed)
        {
            return scopeResult;
        }

        // Resolved the same way MatchIngestion's own post-configure does, so the comparison is
        // between effective lists: an absent MatchIngestion:Platforms means "inherits the shared
        // scope", not "ingests nothing".
        var ingested = platformScope.Resolve(matchIngestionPlatforms);
        var notIngested = Missing(platformScope.Resolve(options.Platforms), ingested);
        return notIngested.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{HarvestOptions.SectionName}:Platforms must be a subset of "
                + $"{MatchIngestionOptions.SectionName}:Platforms — the harvest only sees matches we ingest. "
                + $"Not ingested: {Join(notIngested)}.");
    }

    private ValidateOptionsResult ValidateSectionScope(string? name, string sectionName, List<string> sectionPlatforms)
    {
        if (!IsDefaultInstance(name))
        {
            return ValidateOptionsResult.Skip;
        }

        // Resolve rather than just normalize: the section's post-configure has already replaced an
        // empty list with the shared scope by the time validation runs (PostConfigure precedes
        // Validate in the options pipeline — pinned by PostConfigure_RunsBeforeValidate), so this
        // is idempotent there. Doing it anyway keeps the check correct for any options instance
        // that reaches the validator without that step, instead of reading "no platforms" as a
        // section that merely inherits.
        var active = PlatformNormalizer.Normalize(platformScope.Active);
        var outOfScope = Missing(platformScope.Resolve(sectionPlatforms), active);

        return outOfScope.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{sectionName}:Platforms must be a subset of {ActiveKey}; {Join(outOfScope)} "
                + $"is not active. Add the platform to {ActiveKey} — every section without its own "
                + "list inherits it — instead of overriding a single section.");
    }

    /// <summary>
    /// These options are only ever registered unnamed; a named instance belongs to somebody else
    /// and is skipped rather than validated against the shared scope.
    /// </summary>
    private static bool IsDefaultInstance(string? name)
    {
        return string.IsNullOrEmpty(name);
    }

    private static List<string> Missing(IEnumerable<string> platforms, IEnumerable<string> allowed)
    {
        return platforms.Except(allowed, StringComparer.Ordinal).ToList();
    }

    private static string Join(IEnumerable<string> platforms)
    {
        return string.Join(", ", platforms);
    }
}
