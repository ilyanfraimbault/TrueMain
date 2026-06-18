using Core.Lol.Identifiers;
using Data.Entities;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.Discovery;
using Ingestor.Ranking;
using Ingestor.Riot;
using Ingestor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TrueMain.UnitTests.Fixtures;

namespace TrueMain.UnitTests;

public sealed class DiscoveryProcessNoOpTests
{
    [Fact]
    public async Task RunAsync_WhenPlatformsNormalizeToEmpty_RecordsNoOp()
    {
        var riotPlatformClient = Substitute.For<IRiotPlatformClient>();
        var sessionFactory = Substitute.For<IDataSessionFactory>();
        var runRecorder = Substitute.For<IProcessRunRecorder>();
        var ladderDiscoveryService = Substitute.For<ILadderDiscoveryService>();
        var accountUpsertService = Substitute.For<IAccountUpsertService>();
        var candidateUpsertService = Substitute.For<ICandidateUpsertService>();
        var rankSnapshotWriter = Substitute.For<IRankSnapshotWriter>();

        var process = new DiscoveryProcess(
            NullLogger<DiscoveryProcess>.Instance,
            riotPlatformClient,
            sessionFactory,
            ladderDiscoveryService,
            accountUpsertService,
            candidateUpsertService,
            rankSnapshotWriter,
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new DiscoveryOptions
            {
                Platforms = [" ", "  "]
            }));

        await process.RunRecordedAsync(runRecorder);

        await runRecorder.Received(1).RecordStartAsync(
            "Discovery",
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());

        await runRecorder.Received(1).RecordAsync(
            Arg.Any<Guid>(),
            "Discovery",
            Arg.Any<DateTime>(),
            Arg.Any<DateTime>(),
            ProcessRunStatus.Success,
            Arg.Any<object>(),
            null,
            Arg.Any<CancellationToken>());

        await ladderDiscoveryService.DidNotReceive()
            .DiscoverSummonersAsync(Arg.Any<PlatformRoute>(), Arg.Any<DiscoveryOptions>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
