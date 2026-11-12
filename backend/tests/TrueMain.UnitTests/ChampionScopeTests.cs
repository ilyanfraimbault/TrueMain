using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using TrueMain.Http;
using TrueMain.Services.Champions.Matchups;
using TrueMain.Services.Champions.Scopes;

namespace TrueMain.UnitTests;

public sealed class ChampionScopeTests
{
    [Theory]
    [InlineData("middle", "MIDDLE")]
    [InlineData(" mid ", "MIDDLE")]
    [InlineData("all", "ALL")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void HoldsTheCanonicalPosition(string? position, string? expected)
        => new ChampionScope(1, position, patch: null).Position.Should().Be(expected);

    [Fact]
    public void TwoSpellingsOfOneSliceShareACacheToken()
        => new ChampionScope(1, "mid", "16.4.521.123", "gold_plus").CacheToken()
            .Should().Be(new ChampionScope(1, "MIDDLE", "16.4", "GOLD_PLUS").CacheToken());

    [Theory]
    [InlineData("adc", null)]
    [InlineData("MIDDLE", "GOLDD")]
    public void RejectsAFilterThatNamesNothingRatherThanWideningIt(string? position, string? eloBracket)
    {
        // Widening "adc" to every lane, or "GOLDD" to every band, would answer a labelled
        // question with games nobody asked about.
        var build = () => new ChampionScope(1, position, patch: null, eloBracket);

        build.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("TOP", ChampionPopulation.Truemains, true)]
    [InlineData(null, ChampionPopulation.Truemains, false)]
    [InlineData("ALL", ChampionPopulation.Truemains, false)]
    [InlineData("TOP", ChampionPopulation.Everyone, false)]
    public void AMatchupNeedsALaneAndTheTruemainsPopulation(
        string? position, ChampionPopulation population, bool accepted)
    {
        var champion = new ChampionScope(1, position, patch: null, population: population);

        LaneMatchupScope.TryCreate(champion, opponentChampionId: 2, out var matchup, out var rejection)
            .Should().Be(accepted);
        (matchup is null).Should().Be(!accepted);
        (rejection is null).Should().Be(accepted);
    }

    public static TheoryData<string, string?, string?> Accepted => new()
    {
        { nameof(PositionRule.Required), "top", "TOP" },
        { nameof(PositionRule.RequiredOrAll), "all", "ALL" },
        { nameof(PositionRule.DominantByDefault), null, null },
        { nameof(PositionRule.DominantByDefaultOrAll), "", null },
        { nameof(PositionRule.DominantByDefaultOrAll), "ALL", "ALL" },
        { nameof(PositionRule.EveryByDefault), null, "ALL" },
        { nameof(PositionRule.EveryByDefaultOrAll), " ", "ALL" },
        { nameof(PositionRule.EveryByDefaultOrAll), "all", "ALL" },
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public void TheRouteRuleNamesWhatABlankOrAllPositionMeans(string rule, string? position, string? expected)
    {
        Controller().TryParseChampionScope(1, position, "16.4.1", "gold", Rule(rule), out var scope, out var problem)
            .Should().BeTrue();

        problem.Should().BeNull();
        scope!.Position.Should().Be(expected);
        scope.Patch.Should().Be("16.4");
        scope.EloBracket.Should().Be("GOLD");
    }

    public static TheoryData<string, string?> Rejected => new()
    {
        { nameof(PositionRule.Required), null },
        { nameof(PositionRule.Required), "ALL" },
        { nameof(PositionRule.RequiredOrAll), "" },
        { nameof(PositionRule.DominantByDefault), "ALL" },
        { nameof(PositionRule.EveryByDefault), "all" },
        { nameof(PositionRule.EveryByDefaultOrAll), "adc" },
    };

    [Theory]
    [MemberData(nameof(Rejected))]
    public void APositionTheRouteCannotTakeIsA400(string rule, string? position)
    {
        Controller().TryParseChampionScope(1, position, patch: null, eloBracket: null, Rule(rule), out var scope, out var problem)
            .Should().BeFalse();

        scope.Should().BeNull();
        ProblemDetail(problem).Should().Be(PositionParameter.InvalidMessage);
    }

    [Fact]
    public void AnUnknownBracketIsA400()
    {
        Controller().TryParseChampionScope(
                1, "TOP", patch: null, "GOLDD", PositionRule.Required, out _, out var problem)
            .Should().BeFalse();

        ProblemDetail(problem).Should().Be(EloBracketParameter.InvalidMessage);
    }

    private static PositionRule Rule(string name)
        => (PositionRule)typeof(PositionRule).GetField(name)!.GetValue(null)!;

    private static string? ProblemDetail(ActionResult? problem)
        => ((problem as ObjectResult)?.Value as ProblemDetails)?.Detail;

    private static TestController Controller()
        => new()
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            ProblemDetailsFactory = new DetailOnlyProblemDetailsFactory(),
        };

    private sealed class TestController : ControllerBase;

    private sealed class DetailOnlyProblemDetailsFactory : ProblemDetailsFactory
    {
        public override ProblemDetails CreateProblemDetails(
            HttpContext httpContext,
            int? statusCode = null,
            string? title = null,
            string? type = null,
            string? detail = null,
            string? instance = null)
            => new() { Status = statusCode, Detail = detail };

        public override ValidationProblemDetails CreateValidationProblemDetails(
            HttpContext httpContext,
            ModelStateDictionary modelStateDictionary,
            int? statusCode = null,
            string? title = null,
            string? type = null,
            string? detail = null,
            string? instance = null)
            => new(modelStateDictionary) { Status = statusCode ?? 400, Detail = detail };
    }
}
