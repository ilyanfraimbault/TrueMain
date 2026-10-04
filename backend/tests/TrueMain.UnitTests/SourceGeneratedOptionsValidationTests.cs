using AwesomeAssertions;
using Ingestor.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TrueMain.UnitTests;

/// <summary>
/// The ingestor's range / required rules moved from <c>Validate(...)</c> lambdas to data-annotation
/// attributes checked by <c>[OptionsValidator]</c> source-generated validators (#271). These run the
/// real <see cref="IStartupValidator"/> over the shipped appsettings, so they prove the boot still
/// fails fast — naming the offending option — on every kind of attribute the migration introduced
/// (int bounds, exclusive double bounds, TimeSpan ranges, required strings), that the rules kept as
/// lambdas still fire next to the generated validators, and that the boundary values stay accepted.
/// </summary>
public sealed class SourceGeneratedOptionsValidationTests
{
    [Fact]
    public void Validate_AcceptsTheShippedConfiguration()
    {
        var validate = () => RunStartupValidation();

        validate.Should().NotThrow();
    }

    [Theory]
    // Required string: whitespace counts as missing, as the old IsNullOrWhiteSpace lambda did.
    [InlineData("Riot:ApiKey", " ", "The RiotOptions.ApiKey field is required.")]
    // Inclusive int ranges, at both ends.
    [InlineData("Riot:AttemptTimeoutSeconds", "0", "RiotOptions.AttemptTimeoutSeconds must be between 1 and 600.")]
    [InlineData("RiotRateLimit:MaxPermitWaitSeconds", "3601", "RiotRateLimitOptions.MaxPermitWaitSeconds must be between 1 and 3600.")]
    [InlineData("MatchIngestion:MatchesPerAccount", "101", "MatchIngestionOptions.MatchesPerAccount must be between 1 and 100.")]
    [InlineData("Scoring:TopNPerPlatform", "0", "ScoringOptions.TopNPerPlatform must be between 1 and 2147483647.")]
    [InlineData("MatchDataRetention:AggregateRetainedPatchCount", "-1", "MatchDataRetentionOptions.AggregateRetainedPatchCount must be between 0 and 2147483647.")]
    [InlineData("ItemContextAggregation:MaxAxesPerVerdict", "0", "ItemContextAggregationOptions.MaxAxesPerVerdict must be between 1 and 2147483647.")]
    // Exclusive double bounds.
    [InlineData("RiotRateLimit:SafetyHeadroom", "0.5", "RiotRateLimitOptions.SafetyHeadroom must be between 0 and 0?5 exclusive.")]
    [InlineData("Intake:PromotionHeadroomFactor", "0", "IntakeOptions.PromotionHeadroomFactor must be between 0 exclusive and 100.")]
    [InlineData("Intake:PromotionHeadroomFactor", "100.5", "IntakeOptions.PromotionHeadroomFactor must be between 0 exclusive and 100.")]
    [InlineData("ItemContextAggregation:CoreRate", "0", "ItemContextAggregationOptions.CoreRate must be between 0 exclusive and 1.")]
    [InlineData("ItemContextAggregation:MinAbsoluteLift", "1", "ItemContextAggregationOptions.MinAbsoluteLift must be between 0 exclusive and 1 exclusive.")]
    [InlineData("Scoring:ChampionPointsLogNormalizer", "0", "ScoringOptions.ChampionPointsLogNormalizer must be between 0 exclusive and*")]
    [InlineData("Scoring:RankWeight", "-0.1", "ScoringOptions.RankWeight must be between 0 and*")]
    [InlineData("Harvest:NewCandidateShare", "1.1", "HarvestOptions.NewCandidateShare must be between 0 and 1.")]
    // TimeSpan ranges.
    [InlineData("AccountRefresh:RankSyncFreshness", "-00:00:01", "AccountRefreshOptions.RankSyncFreshness must be between 00:00:00 and*")]
    [InlineData("LadderSync:ApexRefreshInterval", "-1.00:00:00", "LadderSyncOptions.ApexRefreshInterval must be between 00:00:00 and*")]
    // MainAnalysisOptions lives in Core; the ingestor's generated validator still covers it.
    [InlineData("MainAnalysis:MatchesToConsider", "0", "MainAnalysisOptions.MatchesToConsider must be between 1 and 2147483647.")]
    public void Validate_RejectsAttributeViolations(string key, string value, string expectedMessage)
    {
        var validate = () => RunStartupValidation((key, value));

        validate.Should().Throw<OptionsValidationException>().WithMessage($"*{expectedMessage}*");
    }

    [Theory]
    [InlineData("Riot:MaxRetryAttempts", "10")]
    [InlineData("RiotRateLimit:SafetyHeadroom", "0")]
    [InlineData("RiotRateLimit:SafetyHeadroom", "0.49")]
    [InlineData("Intake:PromotionHeadroomFactor", "100")]
    [InlineData("ItemContextAggregation:CoreRate", "1")]
    [InlineData("MatchIngestion:MatchesPerAccount", "100")]
    [InlineData("MatchIngestion:EstablishedMainShare", "0")]
    [InlineData("MatchIngestion:EstablishedMainShare", "1")]
    [InlineData("AccountRefresh:RankSyncFreshness", "00:00:00")]
    [InlineData("LadderSync:MaxRequestsPerRun", "0")]
    public void Validate_AcceptsBoundaryValues(string key, string value)
    {
        var validate = () => RunStartupValidation((key, value));

        validate.Should().NotThrow();
    }

    [Theory]
    [InlineData("Riot:TotalRequestTimeoutSeconds", "5", "Riot:TotalRequestTimeoutSeconds must be >= Riot:AttemptTimeoutSeconds.")]
    [InlineData("Scoring:ScarcityWeight", "5", "Scoring:ScarcityWeight must not exceed recency + rank + points*")]
    [InlineData("ItemContextAggregation:MinPickRate", "0.9", "ItemContextAggregation:MinPickRate must be below CoreRate.")]
    [InlineData("Job:Mode", "Nope", "Job:Mode must be one of:*")]
    public void Validate_KeepsTheCrossFieldRulesAsLambdas(string key, string value, string expectedMessage)
    {
        var validate = () => RunStartupValidation((key, value));

        validate.Should().Throw<OptionsValidationException>().WithMessage($"*{expectedMessage}*");
    }

    [Fact]
    public void Validate_ReportsEveryViolationOfOneSectionTogether()
    {
        var validate = () => RunStartupValidation(
            ("Harvest:MinObservedGames", "0"),
            ("Harvest:LookbackDays", "-1"));

        var exception = validate.Should().Throw<OptionsValidationException>().Which;
        exception.Message.Should()
            .Contain("HarvestOptions.MinObservedGames must be between 1 and")
            .And.Contain("HarvestOptions.LookbackDays must be between 0 and");
    }

    [Fact]
    public void AddValidatedOptions_RegistersEachGeneratedValidatorOnce()
    {
        var services = new ServiceCollection();
        services.AddValidatedOptions(BuildConfiguration([]));
        services.AddValidatedOptions(BuildConfiguration([]));

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IValidateOptions<RiotOptions>>()
            .OfType<RiotOptionsValidator>()
            .Should().ContainSingle();
    }

    private static void RunStartupValidation(params (string Key, string Value)[] overrides)
    {
        var services = new ServiceCollection();
        services.AddValidatedOptions(BuildConfiguration(overrides));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    private static IConfiguration BuildConfiguration((string Key, string Value)[] overrides)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            // Supplied by the environment in every real deployment, and the only value the
            // shipped appsettings.json deliberately leaves blank.
            ["Riot:ApiKey"] = "test-key"
        };

        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "IngestorAppSettings.json"), optional: false)
            .AddInMemoryCollection(values)
            .Build();
    }
}
