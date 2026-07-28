using AwesomeAssertions;
using Ingestor.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TrueMain.UnitTests;

/// <summary>
/// Startup validation for the Riot API client options. These drive the real
/// <see cref="IStartupValidator"/> that <c>ValidateOnStart()</c> registers — the
/// same one the host runs before the ingestor serves anything — so a
/// misconfiguration fails fast with a named error (#855).
/// </summary>
public sealed class RiotOptionsValidationTests
{
    [Fact]
    public void Validate_AcceptsTheShippedConfiguration()
    {
        // Control: the values the ingestor actually ships with must pass, otherwise the
        // rejection tests below would prove nothing.
        var validate = () => RunStartupValidation();

        validate.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(500)]
    public void Validate_RejectsOutOfRangeMaxRetryAttempts(int maxRetryAttempts)
    {
        var validate = () => RunStartupValidation(
            ["Riot:MaxRetryAttempts"], [maxRetryAttempts.ToString()]);

        // Unlike CommunityDragon's handler, Riot's raises the total timeout to fit the
        // attempts rather than shrinking each one — so an unbounded retry count would
        // inflate EffectiveTotalRequestTimeout (and HttpClient.Timeout with it) to an
        // unreasonable length instead of dividing toward zero. Still worth a bound.
        validate.Should().Throw<OptionsValidationException>()
            .WithMessage("*Riot:MaxRetryAttempts must be between 1 and 10.*");
    }

    [Fact]
    public void Validate_AcceptsTheUpperBoundOfMaxRetryAttempts()
    {
        var validate = () => RunStartupValidation(["Riot:MaxRetryAttempts"], ["10"]);

        validate.Should().NotThrow();
    }

    private static void RunStartupValidation(
        IReadOnlyList<string>? keys = null,
        IReadOnlyList<string>? values = null)
    {
        var overrides = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            // Supplied by the environment in every real deployment, and the only value the
            // shipped appsettings.json deliberately leaves blank.
            ["Riot:ApiKey"] = "test-key"
        };

        for (var index = 0; index < (keys?.Count ?? 0); index++)
        {
            overrides[keys![index]] = values![index];
        }

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "IngestorAppSettings.json"), optional: false)
            .AddInMemoryCollection(overrides)
            .Build();

        var services = new ServiceCollection();
        services.AddValidatedOptions(configuration);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();
    }
}
