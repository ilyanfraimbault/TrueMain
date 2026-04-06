using Data.Entities;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace TrueMain.UnitTests;

public sealed class MatchIngestionProcessNoOpTests
{
    [Fact]
    public async Task RunAsync_WhenPlatformsNormalizeToEmpty_RecordsNoOp()
    {
        var sessionFactory = Substitute.For<IDataSessionFactory>();
        var runRecorder = Substitute.For<IProcessRunRecorder>();
        var matchClaimService = Substitute.For<IMatchClaimService>();
        var matchSnapshotWriter = Substitute.For<IMatchSnapshotWriter>();
        var timelineIngestionService = Substitute.For<ITimelineIngestionService>();
        var accountValidationService = Substitute.For<IAccountValidationService>();

        var process = new MatchIngestionProcess(
            NullLogger<MatchIngestionProcess>.Instance,
            sessionFactory,
            runRecorder,
            matchClaimService,
            matchSnapshotWriter,
            timelineIngestionService,
            accountValidationService,
            Options.Create(new MatchIngestionOptions
            {
                Platforms = [" ", "  "]
            }));

        await process.RunAsync(CancellationToken.None);

        await runRecorder.Received(1).RecordAsync(
            "MatchIngestion",
            Arg.Any<DateTime>(),
            Arg.Any<DateTime>(),
            ProcessRunStatus.Success,
            Arg.Any<object>(),
            null,
            Arg.Any<CancellationToken>());

        await matchClaimService.DidNotReceive()
            .ClaimAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }
}
