using Core;
using Data.Entities;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.Discovery;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using Ingestor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

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

        var process = new DiscoveryProcess(
            NullLogger<DiscoveryProcess>.Instance,
            riotPlatformClient,
            sessionFactory,
            runRecorder,
            ladderDiscoveryService,
            accountUpsertService,
            candidateUpsertService,
            Options.Create(new DiscoveryOptions
            {
                Platforms = [" ", "  "]
            }));

        await process.RunAsync(CancellationToken.None);

        await runRecorder.Received(1).RecordAsync(
            "Discovery",
            Arg.Any<DateTime>(),
            Arg.Any<DateTime>(),
            ProcessRunStatus.Success,
            Arg.Any<object>(),
            null,
            Arg.Any<CancellationToken>());

        await ladderDiscoveryService.DidNotReceive()
            .DiscoverSummonersAsync(Arg.Any<PlatformRoute>(), Arg.Any<DiscoveryOptions>(), Arg.Any<CancellationToken>());
    }
}
