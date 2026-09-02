using AwesomeAssertions;
using Data.Entities;
using Ingestor.Processes;

namespace TrueMain.UnitTests;

/// <summary>
/// The snapshot's zero-fill (#1403). A status holding no rows produces no group, and a
/// missing document is how the read side spells "this period was never measured" — so
/// the healthy states, <c>New</c> and <c>Processing</c> at 0, have to be written
/// explicitly or they would be indistinguishable from an ingestor that was down.
/// </summary>
public sealed class CandidateStockSnapshotProcessTests
{
    [Fact]
    public void BuildSamples_WritesEveryStatusForEveryObservedPlatform()
    {
        var samples = CandidateStockSnapshotProcess.BuildSamples(
        [
            ("EUW1", MainCandidateStatus.Queued, 300),
            ("KR", MainCandidateStatus.Scored, 40)
        ]);

        var statusCount = Enum.GetValues<MainCandidateStatus>().Length;
        samples.Should().HaveCount(2 * statusCount);
        samples.Should().Contain(sample => sample.PlatformId == "EUW1" && sample.Status == "New" && sample.Count == 0);
        samples.Should().Contain(sample => sample.PlatformId == "KR" && sample.Status == "Queued" && sample.Count == 0);
        samples.Single(sample => sample.PlatformId == "EUW1" && sample.Status == "Queued").Count.Should().Be(300);
        samples.Single(sample => sample.PlatformId == "KR" && sample.Status == "Scored").Count.Should().Be(40);
    }

    [Fact]
    public void BuildSamples_InventsNoPlatform_WhenTheTableIsEmpty()
    {
        CandidateStockSnapshotProcess.BuildSamples([]).Should()
            .BeEmpty("a platform holding no candidates has nothing to report about the funnel");
    }

    [Fact]
    public void BuildSamples_NamesStatusesByName_SoTheDocumentsDoNotEncodeTheEnumNumbering()
    {
        var samples = CandidateStockSnapshotProcess.BuildSamples([("EUW1", MainCandidateStatus.Validated, 1)]);

        samples.Select(sample => sample.Status).Should()
            .BeEquivalentTo(Enum.GetNames<MainCandidateStatus>());
    }
}
