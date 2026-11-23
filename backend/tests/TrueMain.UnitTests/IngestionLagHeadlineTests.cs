using AwesomeAssertions;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.DataQuality;
using static TrueMain.Services.Ops.DataQuality.IngestionLagDetector;

namespace TrueMain.UnitTests;

/// <summary>
/// The ingestion card's sentence is what the operator reads first when the admin tab
/// turns amber, so it must name what tripped it (#2459): a queue past its line, not a
/// count of zero lagging platforms.
/// </summary>
public sealed class IngestionLagHeadlineTests
{
    private static QueueDepth[] Queues(long timelines = 0, long queued = 0, long processing = 0) =>
    [
        new("Matches awaiting a timeline", timelines, 5_000, 25_000),
        new("Candidates queued", queued, 20_000, 100_000),
        new("Candidates processing", processing, 500, 5_000)
    ];

    [Fact]
    public void Headline_NamesTheQueue_WhenOnlyAQueueTrippedTheVerdict()
    {
        var headline = IngestionLagDetector.Headline(DetectorStatus.Amber, 0, Queues(queued: 24_000));

        headline.Should().Be("24,000 candidates queued (amber from 20,000).");
        headline.Should().NotContain("platform");
    }

    [Fact]
    public void Headline_QuotesTheRedLine_WhenAQueueIsRed()
    {
        IngestionLagDetector.Headline(DetectorStatus.Red, 0, Queues(timelines: 30_000))
            .Should().Be("30,000 matches awaiting a timeline (red from 25,000).");
    }

    [Fact]
    public void Headline_CountsPlatforms_WhenOnlyPlatformsLag()
    {
        IngestionLagDetector.Headline(DetectorStatus.Amber, 1, Queues())
            .Should().Be("1 platform has not ingested a recent match.");
        IngestionLagDetector.Headline(DetectorStatus.Red, 3, Queues())
            .Should().Be("3 platforms have not ingested a recent match.");
    }

    [Fact]
    public void Headline_ListsEveryCause_WhenPlatformsAndQueuesBothFire()
    {
        IngestionLagDetector.Headline(DetectorStatus.Red, 2, Queues(queued: 20_000, processing: 6_000))
            .Should().Be("2 platforms have not ingested a recent match; "
                + "20,000 candidates queued (amber from 20,000); "
                + "6,000 candidates processing (red from 5,000).");
    }

    [Fact]
    public void Headline_KeepsTheVerdictSentence_WhenNothingFired()
    {
        IngestionLagDetector.Headline(DetectorStatus.Green, 0, Queues())
            .Should().Contain("Every platform has ingested");
        IngestionLagDetector.Headline(DetectorStatus.Unknown, 0, Queues())
            .Should().Contain("could not be measured");
    }
}
