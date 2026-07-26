using AwesomeAssertions;
using Ingestor.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TrueMain.UnitTests;

/// <summary>
/// Guards the shipped ingestor appsettings.json against the documentation-only sections
/// it carries. "Riot" and "CommunityDragon" are written as empty objects purely so an
/// operator reading the file can see the key exists and is tunable; the values themselves
/// live as defaults on the options classes. This locks in that the empty objects stay
/// inert: the JSON provider maps an empty object to a null-valued key with no children,
/// so Bind() finds nothing to write and the code defaults survive untouched.
/// </summary>
public sealed class IngestorAppSettingsBindingTests
{
    [Fact]
    public void EmptyCommunityDragonSection_LeavesCodeDefaultsIntact()
    {
        var options = ResolveFromShippedConfiguration<CommunityDragonOptions>();

        // Resolving .Value runs the validators registered in AddValidatedOptions, so an
        // empty section zeroing these out would fail here instead of at ingestor startup.
        options.MaxRetryAttempts.Should().Be(3);
        options.AttemptTimeoutSeconds.Should().Be(15);
        options.TotalRequestTimeoutSeconds.Should().Be(75);
    }

    [Fact]
    public void ShippedAppSettings_DeclaresTheCommunityDragonSection()
    {
        var configuration = BuildShippedConfiguration();

        // Discoverability, mirroring the neighbouring "Riot" section. Note this cannot be
        // asserted with Exists(): an empty object has a null value and no children, which
        // is exactly why it is safe to add — the section is visible to a reader of the
        // file and invisible to the binder.
        configuration.GetChildren().Select(section => section.Key)
            .Should().Contain(CommunityDragonOptions.SectionName);
    }

    private static TOptions ResolveFromShippedConfiguration<TOptions>()
        where TOptions : class
    {
        var services = new ServiceCollection();
        services.AddValidatedOptions(BuildShippedConfiguration());

        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<TOptions>>().Value;
    }

    private static IConfigurationRoot BuildShippedConfiguration()
    {
        // The ingestor's real appsettings.json, copied to the test output under a distinct
        // name by the project file so it cannot be mistaken for the test host's own.
        var path = Path.Combine(AppContext.BaseDirectory, "IngestorAppSettings.json");

        return new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();
    }
}
