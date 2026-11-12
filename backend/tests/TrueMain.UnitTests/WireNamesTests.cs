using AwesomeAssertions;
using Data.Entities;
using TrueMain.ReadModels;
using TrueMain.ReadModels.Ops;

namespace TrueMain.UnitTests;

public sealed class WireNamesTests
{
    [Fact]
    public void WireName_SpellsAValueAsTheApiWritesIt()
    {
        DetectorStatus.Red.WireName().Should().Be("red");
        PersonaConflictBulkSkipReason.NotFound.WireName().Should().Be("notFound");
        ProcessRunStatus.Abandoned.WireName().Should().Be("Abandoned");
    }

    [Theory]
    [InlineData("recovered", HealthAlertChangeKind.Recovered)]
    [InlineData("failing", HealthAlertChangeKind.Failing)]
    public void ParseWireName_ReadsBackWhatWireNameWrote(string name, HealthAlertChangeKind expected)
        => WireNames.ParseWireName(name, HealthAlertChangeKind.Failing).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("exploded")]
    public void ParseWireName_FallsBack_WhenTheTextNamesNoValue(string? name)
        => WireNames.ParseWireName(name, DetectorStatus.Unknown).Should().Be(DetectorStatus.Unknown);
}
