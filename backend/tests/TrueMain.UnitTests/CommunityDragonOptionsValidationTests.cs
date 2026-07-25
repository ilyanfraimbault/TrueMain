using AwesomeAssertions;
using Ingestor.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TrueMain.UnitTests;

/// <summary>
/// Startup validation for the CommunityDragon client options. These drive the real
/// <see cref="IStartupValidator"/> that <c>ValidateOnStart()</c> registers — the same one
/// the host runs before the ingestor serves anything — so a misconfiguration fails fast
/// with a named error instead of reaching the resilience handler, where an unusable
/// per-attempt timeout would surface as an opaque startup crash-loop.
/// </summary>
public sealed class CommunityDragonOptionsValidationTests
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
            ["CommunityDragon:MaxRetryAttempts"], [maxRetryAttempts.ToString()]);

        // A count of several hundred would drive the handler's
        // total / (MaxRetryAttempts + 1) division toward TimeSpan.Zero; the operator gets
        // this message at startup instead.
        validate.Should().Throw<OptionsValidationException>()
            .WithMessage("*CommunityDragon:MaxRetryAttempts must be between 1 and 10.*");
    }

    [Fact]
    public void Validate_AcceptsTheUpperBoundOfMaxRetryAttempts()
    {
        // The bound must not constrain a legitimate operator: the highest allowed retry
        // count still works against the shipped 75s total (each attempt keeps ~6.8s).
        var validate = () => RunStartupValidation(["CommunityDragon:MaxRetryAttempts"], ["10"]);

        validate.Should().NotThrow();
    }

    [Fact]
    public void Validate_RejectsATotalBudgetTooSmallForTheRetryCount()
    {
        // In range individually, nonsensical together: 10 attempts inside 5s would leave
        // each one under half a second — far too short for a multi-megabyte payload.
        var validate = () => RunStartupValidation(
            ["CommunityDragon:MaxRetryAttempts", "CommunityDragon:AttemptTimeoutSeconds", "CommunityDragon:TotalRequestTimeoutSeconds"],
            ["10", "2", "5"]);

        validate.Should().Throw<OptionsValidationException>()
            .WithMessage("*CommunityDragon:TotalRequestTimeoutSeconds must be >= CommunityDragon:MaxRetryAttempts + 1*");
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
