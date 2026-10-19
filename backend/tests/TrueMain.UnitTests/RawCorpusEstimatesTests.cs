using AwesomeAssertions;
using TrueMain.Services.Ops.Health;

namespace TrueMain.UnitTests;

/// <summary>
/// The cockpit's raw-corpus block reads the planner's row estimates (#2132). A table the
/// statistics view does not list must read as "not measured", never as an empty corpus.
/// </summary>
public sealed class RawCorpusEstimatesTests
{
    [Fact]
    public void From_ReadsBothTables_WhenTheyAreListed()
    {
        var estimates = PipelineHealthQueryService.RawCorpusEstimates.From(
        [
            new("matches", 120_000),
            new("match_participants", 1_200_000),
            new("riot_accounts", 50_000)
        ]);

        estimates.Matches.Should().Be(120_000);
        estimates.Participants.Should().Be(1_200_000);
        estimates.UnknownReason.Should().BeNull();
    }

    [Fact]
    public void From_ReportsAReason_RatherThanZero_WhenATableIsMissing()
    {
        var estimates = PipelineHealthQueryService.RawCorpusEstimates.From([new("matches", 120_000)]);

        estimates.Participants.Should().BeNull();
        estimates.UnknownReason.Should().NotBeNullOrWhiteSpace();
    }
}
