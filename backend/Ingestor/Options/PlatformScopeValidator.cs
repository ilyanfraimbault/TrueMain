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
/// <param name="matchIngestionOptions">Ingested platforms, for the harvest subset check.</param>
internal sealed class PlatformScopeValidator(
    PlatformScopeOptions platformScope,
    IOptions<MatchIngestionOptions> matchIngestionOptions) :
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

        // MatchIngestion is read through IOptions here, so its own validation runs first; when it
        // fails, that failure is already reported on its own and repeating it per section would
        // only bury the actionable message. MatchIngestion never reads Harvest back, so the two
        // option types cannot cycle.
        List<string> ingested;
        try
        {
            ingested = platformScope.Resolve(matchIngestionOptions.Value.Platforms);
        }
        catch (OptionsValidationException)
        {
            return ValidateOptionsResult.Skip;
        }

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
