using AwesomeAssertions;
using Ingestor.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TrueMain.UnitTests;

/// <summary>
/// Covers <see cref="DiscoveryOptions.TierScope"/> (#860): it used to default to
/// <c>{Master, GM, Challenger}</c>, and <see cref="ConfigurationBinder"/> appends bound
/// entries to a list that already has items — so an operator narrowing the scope in
/// configuration silently got the hard-coded default unioned back in. The default is now
/// empty, the same fix #496/#854 applied to <c>Platforms:Active</c>, so a narrowing
/// override actually narrows and an empty (unconfigured) scope fails startup instead of
/// quietly falling back to the old tiers.
/// </summary>
public sealed class DiscoveryTierScopeValidationTests
{
    private const string ApiKeyOverride = "Riot:ApiKey";

    [Fact]
    public void CommittedIngestorConfiguration_PassesStartupValidation()
    {
        var validate = () => ValidateOnStart(BuildCommittedConfiguration());

        validate.Should().NotThrow();
    }

    [Fact]
    public void NarrowingOverride_YieldsExactlyTheConfiguredTiers()
    {
        // The bug this pins: before #860, this override would have bound to
        // {"Challenger", "Master", "GM", "Challenger"} — the operator's single tier
        // unioned with the old hard-coded default — because the default list already
        // had items for ConfigurationBinder to append onto.
        using var provider = BuildProvider(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Discovery:TierScope:0"] = "Challenger"
        }));

        provider.GetRequiredService<IOptions<DiscoveryOptions>>().Value.TierScope
            .Should().BeEquivalentTo(["Challenger"]);
    }

    [Fact]
    public void EmptyTierScope_FailsStartupValidation()
    {
        var validate = () => ValidateOnStart(BuildConfiguration(new Dictionary<string, string?>()));

        validate.Should().Throw<OptionsValidationException>()
            .WithMessage("*Discovery:TierScope must contain at least one value.*");
    }

    [Fact]
    public void UnknownTier_FailsStartupValidation()
    {
        // A typo (e.g. "Diamond", or "Grand Master" with a space) previously matched
        // nothing in LadderDiscoveryService.FetchLadderEntriesAsync and was silently
        // skipped — no warning, no error, just a smaller ladder scan than configured.
        var validate = () => ValidateOnStart(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Discovery:TierScope:0"] = "Diamond"
        }));

        validate.Should().Throw<OptionsValidationException>()
            .WithMessage("*Discovery:TierScope must contain only Master, GM (or Grandmaster) and/or Challenger*");
    }

    [Theory]
    [InlineData("GM")]
    [InlineData("Grandmaster")]
    [InlineData("master")]
    [InlineData("CHALLENGER")]
    public void KnownTierSynonymsAndCasing_PassStartupValidation(string tier)
    {
        var validate = () => ValidateOnStart(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Discovery:TierScope:0"] = tier
        }));

        validate.Should().NotThrow();
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
        // Unrelated to what these tests exercise, but Platforms:Active (#496/#854) also
        // fails startup validation when unconfigured. TryAdd so the EmptyTierScope test
        // (which deliberately supplies no TierScope) still fails for the reason it targets.
        settings.TryAdd("Platforms:Active:0", "KR");
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private static IConfiguration BuildCommittedConfiguration()
    {
        // Linked by the csproj to backend/Ingestor/appsettings.json, so the file that
        // actually ships is the one being validated here.
        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "ingestor.appsettings.json"), optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?> { [ApiKeyOverride] = "test-key" })
            .Build();
    }
}
