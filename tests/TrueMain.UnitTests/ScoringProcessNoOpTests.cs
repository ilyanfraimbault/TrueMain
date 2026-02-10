using Data.Entities;
using Data.Repositories;
using FluentAssertions;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace TrueMain.UnitTests;

public sealed class ScoringProcessNoOpTests
{
    [Fact]
    public async Task RunAsync_WhenNoCandidates_RecordsNoOpRun()
    {
        var sessionFactory = Substitute.For<IDataSessionFactory>();
        var session = Substitute.For<IDataSession>();
        var mainCandidates = Substitute.For<IMainCandidateRepository>();
        var runRecorder = Substitute.For<IProcessRunRecorder>();

        mainCandidates.GetNewBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<MainCandidate>()));

        session.MainCandidates.Returns(mainCandidates);
        sessionFactory.CreateAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(session));

        var process = new ScoringProcess(
            NullLogger<ScoringProcess>.Instance,
            sessionFactory,
            runRecorder,
            Options.Create(new ScoringOptions()));

        await process.RunAsync(CancellationToken.None);

        await runRecorder.Received(1).RecordAsync(
            "Scoring",
            Arg.Any<DateTime>(),
            Arg.Any<DateTime>(),
            ProcessRunStatus.Success,
            Arg.Any<object>(),
            null,
            Arg.Any<CancellationToken>());

        await session.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
