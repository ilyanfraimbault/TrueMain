using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using AwesomeAssertions;
using Core.Options;
using Ingestor.Options;
using Microsoft.Extensions.Configuration;

namespace TrueMain.UnitTests;

/// <summary>
/// The charter's configuration rule, enforced: a default belongs to its <c>*Options</c> class and
/// is never restated in <c>appsettings.json</c>.
///
/// <para>
/// The rule is not cosmetic. The admin /configuration page (#1034) tags every value as a default
/// or as an override with the provider it came from, so a key that merely repeats its class
/// default reads as a deliberate override and makes the distinction useless — and, worse, buries
/// the keys that genuinely do override something. That is exactly how <c>Discovery:MaxAccountsPerPlatformPerRun</c>
/// sat at 500 against a class default of 350 for months without anyone noticing which one was in
/// force.
/// </para>
///
/// <para>
/// Scope: the pipeline options the ingestor binds. Sections that are not options classes
/// (<c>Logging</c>), that are documentation-only empty objects (<c>Riot</c>,
/// <c>CommunityDragon</c>, <c>Job</c>) and list-valued properties are out of scope — a list
/// default deliberately stays empty in the class and ships its real value in JSON, because the
/// configuration binder <em>appends</em> to a non-empty list instead of replacing it (#860).
/// </para>
/// </summary>
public sealed class IngestorAppSettingsNoDefaultsTests
{
    private static readonly (string Section, Type OptionsType)[] BoundSections =
    [
        (DiscoveryOptions.SectionName, typeof(DiscoveryOptions)),
        (ManualSeedOptions.SectionName, typeof(ManualSeedOptions)),
        (ScoringOptions.SectionName, typeof(ScoringOptions)),
        (HarvestOptions.SectionName, typeof(HarvestOptions)),
        (CoverageOptions.SectionName, typeof(CoverageOptions)),
        (MatchIngestionOptions.SectionName, typeof(MatchIngestionOptions)),
        (MainActivityOptions.SectionName, typeof(MainActivityOptions)),
        ("MainAnalysis", typeof(MainAnalysisOptions)),
        (AccountRefreshOptions.SectionName, typeof(AccountRefreshOptions)),
        (MatchDataRetentionOptions.SectionName, typeof(MatchDataRetentionOptions)),
        (CandidatePruningOptions.SectionName, typeof(CandidatePruningOptions)),
        (PowerspikeAggregationOptions.SectionName, typeof(PowerspikeAggregationOptions)),
        (MatchupLeadAggregationOptions.SectionName, typeof(MatchupLeadAggregationOptions)),
        (SynergyAggregationOptions.SectionName, typeof(SynergyAggregationOptions)),
        (LaneOutcomeAggregationOptions.SectionName, typeof(LaneOutcomeAggregationOptions)),
        (BanAggregationOptions.SectionName, typeof(BanAggregationOptions)),
        (PlatformScopeOptions.SectionName, typeof(PlatformScopeOptions))
    ];

    [Fact]
    public void NoShippedKeyRepeatsItsClassDefault()
    {
        var configuration = BuildShippedConfiguration();

        var redundant = new List<string>();

        foreach (var (sectionName, optionsType) in BoundSections)
        {
            var section = configuration.GetSection(sectionName);
            var defaults = Activator.CreateInstance(optionsType)!;

            foreach (var property in optionsType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!IsScalar(property.PropertyType))
                {
                    continue;
                }

                var configured = section[property.Name];
                if (configured is null)
                {
                    continue;
                }

                var defaultValue = property.GetValue(defaults);
                if (defaultValue is not null && Equals(Convert(configured, property.PropertyType), defaultValue))
                {
                    redundant.Add($"{sectionName}:{property.Name} = {configured}");
                }
            }
        }

        redundant.Should().BeEmpty(
            "appsettings.json must only carry values that differ from the class default — a key "
            + "repeating its default reads as an override on the admin /configuration page");
    }

    /// <summary>
    /// The section the rule was written for: it is allowed to be absent, but never to restate the
    /// class default. Spelled out on its own so a regression names the section that broke.
    /// </summary>
    [Fact]
    public void DiscoveryDoesNotRestateItsAccountsPerRunDefault()
    {
        var configured = BuildShippedConfiguration()[
            $"{DiscoveryOptions.SectionName}:{nameof(DiscoveryOptions.MaxAccountsPerPlatformPerRun)}"];

        configured.Should().BeNull(
            "the ladder-scan width lives on DiscoveryOptions; the JSON copy is what made the real "
            + "value ambiguous for months");
    }

    private static bool IsScalar(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying.IsPrimitive
               || underlying.IsEnum
               || underlying == typeof(string)
               || underlying == typeof(decimal)
               || underlying == typeof(TimeSpan);
    }

    private static object? Convert(string raw, Type targetType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        try
        {
            return TypeDescriptor.GetConverter(underlying).ConvertFromString(null, CultureInfo.InvariantCulture, raw);
        }
        catch (Exception)
        {
            // An unconvertible value is not this test's business: the binder (and startup
            // validation) fails on it far more loudly than an equality check here would.
            return null;
        }
    }

    private static IConfigurationRoot BuildShippedConfiguration()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "IngestorAppSettings.json");

        return new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();
    }
}
