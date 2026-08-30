using AwesomeAssertions;
using Ingestor.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TrueMain.UnitTests;

/// <summary>
/// Covers <c>MainAnalysis:PlayRateFloor</c>'s upper bound (PR #930 review
/// follow-up): the ingestor used to accept exactly <c>1</c>, disagreeing with
/// the Api's stricter <c>&lt; 1</c> on the same configuration section —
/// <see cref="Core.Truemains.DedicationScore.Commitment"/> divides by
/// <c>(1 - floor)</c>, so a floor of exactly 1 would divide by zero there. Both
/// hosts now reject it.
/// </summary>
public sealed class MainAnalysisOptionsValidationTests
{
    private const string ApiKeyOverride = "Riot:ApiKey";
    private const string TierScopeOverride = "Discovery:TierScope:0";
    private const string PlatformOverride = "Platforms:Active:0";
    private const string LadderSyncTierScopeOverride = "LadderSync:TierScope:0";

    [Fact]
    public void PlayRateFloorOfExactlyOne_FailsStartupValidation()
    {
        var validate = () => RunStartupValidation("MainAnalysis:PlayRateFloor", "1");

        validate.Should().Throw<OptionsValidationException>()
            .WithMessage("*MainAnalysis:PlayRateFloor must be in [0, 1)*");
    }

    [Fact]
    public void PlayRateFloorJustUnderOne_PassesStartupValidation()
    {
        var validate = () => RunStartupValidation("MainAnalysis:PlayRateFloor", "0.999");

        validate.Should().NotThrow();
    }

    private static void RunStartupValidation(string key, string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ApiKeyOverride] = "test-key",
                [TierScopeOverride] = "Master",
                [PlatformOverride] = "KR",
                [LadderSyncTierScopeOverride] = "Master",
                // PlayRateThreshold defaults to 0.2; a floor at 0.999 must not also
                // trip the separate "floor <= threshold" cross-check.
                ["MainAnalysis:PlayRateThreshold"] = "1",
                [key] = value,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddValidatedOptions(configuration);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IStartupValidator>().Validate();
    }
}
