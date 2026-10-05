using System.Text.Json;
using AwesomeAssertions;
using Core.Lol.WinProbability;

namespace TrueMain.UnitTests;

/// <summary>
/// Holds the C# port of the win-probability builder (<see cref="WinProbabilityBuilder"/>) to the TS
/// one (<c>web/layers/common/app/utils/win-probability-timeline.ts</c>) through the shared fixture
/// <c>web/shared/fixtures/win-probability-timeline.json</c> (#1911): its <c>expected</c> is the TS
/// builder's output for its <c>timeline</c>. A weight or a step changed on one side only fails here.
/// </summary>
public sealed class WinProbabilityParityTests
{
    private const double Tolerance = 1e-9;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static (WinProbabilityTimeline Timeline, WinProbabilityCurve Expected) LoadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "win-probability-timeline.json");
        var fixture = JsonSerializer.Deserialize<Fixture>(File.ReadAllText(path), JsonOptions)!;
        // A fixture that deserialized to empty lists would make every comparison below vacuous.
        fixture.Timeline.Events.Should().NotBeEmpty();
        fixture.Expected.Points.Should().NotBeEmpty();
        fixture.Expected.Swings.Should().NotBeEmpty();
        fixture.Expected.Objectives.Should().NotBeEmpty();
        return (fixture.Timeline, fixture.Expected);
    }

    [Fact]
    public void Build_ReproducesTheTsBuildersCurve()
    {
        var (timeline, expected) = LoadFixture();

        var actual = WinProbabilityBuilder.Build(timeline);

        actual.Should().NotBeNull();
        actual!.Points.Select(point => point.Ms).Should().Equal(expected.Points.Select(point => point.Ms));
        for (var i = 0; i < expected.Points.Count; i++)
        {
            actual.Points[i].P.Should().BeApproximately(expected.Points[i].P, Tolerance, "point {0} at {1} ms", i, expected.Points[i].Ms);
        }
    }

    [Fact]
    public void Build_ReproducesTheTsBuildersSwings()
    {
        var (timeline, expected) = LoadFixture();

        var actual = WinProbabilityBuilder.Build(timeline)!;

        actual.Swings.Should().HaveCount(expected.Swings.Count);
        for (var i = 0; i < expected.Swings.Count; i++)
        {
            actual.Swings[i].Delta.Should().BeApproximately(expected.Swings[i].Delta, Tolerance, "swing {0}", i);
            actual.Swings[i].Should().BeEquivalentTo(expected.Swings[i], options => options.Excluding(swing => swing.Delta), "swing {0}", i);
        }
    }

    [Fact]
    public void Build_ReproducesTheTsBuildersObjectives()
    {
        var (timeline, expected) = LoadFixture();

        var actual = WinProbabilityBuilder.Build(timeline)!;

        actual.Objectives.Should().HaveCount(expected.Objectives.Count);
        for (var i = 0; i < expected.Objectives.Count; i++)
        {
            if (expected.Objectives[i].Delta is { } delta)
            {
                actual.Objectives[i].Delta.Should().BeApproximately(delta, Tolerance, "objective {0}", i);
            }
            else
            {
                actual.Objectives[i].Delta.Should().BeNull("objective {0} is not weighed", i);
            }

            actual.Objectives[i].Should().BeEquivalentTo(expected.Objectives[i], options => options.Excluding(objective => objective.Delta), "objective {0}", i);
        }
    }

    [Fact]
    public void Build_ReturnsNull_UnderFifteenMinutes()
    {
        var (timeline, _) = LoadFixture();

        WinProbabilityBuilder.Build(timeline with { DurationMs = WinProbabilityBuilder.MinDurationMs - 1 }).Should().BeNull();
    }

    [Fact]
    public void Build_ReturnsNull_WithoutTheFiveLanesPaired()
    {
        var (timeline, _) = LoadFixture();
        var participants = timeline.Participants
            .Select(participant => participant.ParticipantId == 10 ? participant with { Position = "BOTTOM" } : participant)
            .ToList();

        WinProbabilityBuilder.Build(timeline with { Participants = participants }).Should().BeNull();
    }

    [Fact]
    public void Build_ReturnsNull_WithoutFrames()
    {
        var (timeline, _) = LoadFixture();

        WinProbabilityBuilder.Build(timeline with { Frames = [] }).Should().BeNull();
    }

    private sealed record Fixture(WinProbabilityTimeline Timeline, WinProbabilityCurve Expected);
}
