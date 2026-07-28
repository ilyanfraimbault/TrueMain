using AwesomeAssertions;
using Ingestor.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TrueMain.UnitTests;

/// <summary>
/// Covers the shared platform scope (#496): every pipeline section inherits <c>Platforms:Active</c>
/// unless it declares an explicit narrowing override, and a divergent override fails the boot
/// through <c>ValidateOnStart</c> instead of silently skipping a region for one stage.
/// </summary>
public class PlatformScopeValidationTests
{
    /// <summary>
    /// The Riot key is supplied per environment (env var / secret), never committed, so the
    /// committed configuration needs it stubbed to reach the platform validation.
    /// </summary>
    private const string ApiKeyOverride = "Riot:ApiKey";

    [Fact]
    public void CommittedIngestorConfiguration_PassesStartupValidation()
    {
        var configuration = BuildCommittedConfiguration();

        var validate = () => ValidateOnStart(configuration);

        validate.Should().NotThrow();
    }

    [Fact]
    public void CommittedIngestorConfiguration_GivesEverySectionTheSharedPlatformList()
    {
        using var provider = BuildProvider(BuildCommittedConfiguration());

        var active = provider.GetRequiredService<IOptions<PlatformScopeOptions>>().Value.Active;

        active.Should().NotBeEmpty();
        EffectivePlatforms(provider).Should().AllBeEquivalentTo(active);
    }

    [Fact]
    public void SectionWithoutOverride_InheritsTheActivePlatforms()
    {
        using var provider = BuildProvider(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Platforms:Active:0"] = " euw1 ",
            ["Platforms:Active:1"] = "KR",
            ["Platforms:Active:2"] = "EUW1"
        }));

        // Normalized on the way in: trimmed, upper-cased and deduplicated.
        EffectivePlatforms(provider).Should().AllBeEquivalentTo(new[] { "EUW1", "KR" });
    }

    [Fact]
    public void SectionOverride_NarrowsOnlyThatSection()
    {
        using var provider = BuildProvider(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Platforms:Active:0"] = "KR",
            ["Platforms:Active:1"] = "EUW1",
            ["Harvest:Platforms:0"] = "KR"
        }));

        provider.GetRequiredService<IOptions<HarvestOptions>>().Value.Platforms.Should().Equal("KR");
        provider.GetRequiredService<IOptions<DiscoveryOptions>>().Value.Platforms.Should().Equal("KR", "EUW1");
        provider.GetRequiredService<IOptions<MatchIngestionOptions>>().Value.Platforms.Should().Equal("KR", "EUW1");
    }

    [Fact]
    public void SectionOverrideOutsideTheActiveScope_FailsStartupValidation()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Platforms:Active:0"] = "KR",
            ["Platforms:Active:1"] = "EUW1",
            // The silent-divergence case: a region added to one section only.
            ["Discovery:Platforms:0"] = "KR",
            ["Discovery:Platforms:1"] = "NA1"
        });

        var validate = () => ValidateOnStart(configuration);

        validate.Should().Throw<OptionsValidationException>()
            .WithMessage("*Discovery:Platforms must be a subset of Platforms:Active*NA1*");
    }

    [Fact]
    public void HarvestOutsideTheIngestedPlatforms_FailsStartupValidation()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Platforms:Active:0"] = "KR",
            ["Platforms:Active:1"] = "EUW1",
            ["MatchIngestion:Platforms:0"] = "KR",
            // Both sections narrow within the active scope, but the harvest would mine a
            // platform whose matches are never ingested.
            ["Harvest:Platforms:0"] = "EUW1"
        });

        var validate = () => ValidateOnStart(configuration);

        validate.Should().Throw<OptionsValidationException>()
            .WithMessage("*Harvest:Platforms must be a subset of MatchIngestion:Platforms*EUW1*");
    }

    [Fact]
    public void EmptyActiveScope_FailsStartupValidation()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Platforms:Active:0"] = "   "
        });

        var validate = () => ValidateOnStart(configuration);

        validate.Should().Throw<OptionsValidationException>()
            .WithMessage("*Platforms:Active must contain at least one value*");
    }

    [Fact]
    public void UnknownPlatformInTheActiveScope_FailsStartupValidation()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Platforms:Active:0"] = "KR",
            // "EUW" is not a Riot platform id ("EUW1" is); it would be skipped at runtime
            // with nothing but a warning.
            ["Platforms:Active:1"] = "EUW"
        });

        var validate = () => ValidateOnStart(configuration);

        validate.Should().Throw<OptionsValidationException>()
            .WithMessage("*Platforms:Active contains unknown platform id(s): EUW*");
    }

    /// <summary>
    /// Pins the options-pipeline ordering the validator's messages describe: the section lists are
    /// resolved against the shared scope in <c>PostConfigure</c>, and validation must therefore see
    /// the effective list rather than the raw configured one. The validator stays correct either
    /// way (its <c>Resolve</c> call is idempotent), but the error messages it produces would name
    /// the wrong list if this ever flipped.
    /// </summary>
    [Fact]
    public void PostConfigure_RunsBeforeValidate()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddOptions<OrderProbeOptions>()
            .PostConfigure(_ => calls.Add("post-configure"))
            .Validate(_ =>
            {
                calls.Add("validate");
                return true;
            });

        using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IOptions<OrderProbeOptions>>().Value;

        calls.Should().Equal("post-configure", "validate");
    }

    private static List<List<string>> EffectivePlatforms(IServiceProvider provider)
    {
        return
        [
            provider.GetRequiredService<IOptions<DiscoveryOptions>>().Value.Platforms,
            provider.GetRequiredService<IOptions<MatchIngestionOptions>>().Value.Platforms,
            provider.GetRequiredService<IOptions<HarvestOptions>>().Value.Platforms
        ];
    }

    private static void ValidateOnStart(IConfiguration configuration)
    {
        using var provider = BuildProvider(configuration);
        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    private static ServiceProvider BuildProvider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddValidatedOptions(configuration);
        return services.BuildServiceProvider();
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> settings)
    {
        settings[ApiKeyOverride] = "test-key";
        // Unrelated to what these tests exercise (Platforms:Active), but Discovery:TierScope
        // (#860) now fails startup validation when unconfigured — same reason ApiKeyOverride
        // is stubbed above. TryAdd so a test that sets its own TierScope keeps them.
        settings.TryAdd("Discovery:TierScope:0", "Master");
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private static IConfiguration BuildCommittedConfiguration()
    {
        // Linked by the csproj to backend/Ingestor/appsettings.json, so the file that actually
        // ships is the one being validated here.
        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "ingestor.appsettings.json"), optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?> { [ApiKeyOverride] = "test-key" })
            .Build();
    }

    /// <summary>Empty options type used only to observe the options pipeline ordering.</summary>
    private sealed class OrderProbeOptions;
}
