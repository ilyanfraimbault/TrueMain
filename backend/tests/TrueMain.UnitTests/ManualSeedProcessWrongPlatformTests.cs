using System.Net;
using AwesomeAssertions;
using Core.Lol.Identifiers;
using Data.Logging;
using Data.Logging.Mongo;
using Data.Ops.Mongo;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.Discovery;
using Ingestor.Processes.Summaries;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace TrueMain.UnitTests;

/// <summary>
/// A Riot ID that resolves on the regional cluster but has no summoner on the submitted
/// platform is an expected outcome of a wrong platform, not an unexpected failure (#2133):
/// a readable error on the request, no stack trace in the ops log, its own counter.
/// </summary>
public sealed class ManualSeedProcessWrongPlatformTests
{
    private readonly SeedRequestDocument _request = new()
    {
        Id = Guid.NewGuid(),
        GameName = "Faker",
        TagLine = "CN1",
        PlatformId = "BR1"
    };

    private readonly ISeedRequestStore _store = Substitute.For<ISeedRequestStore>();
    private readonly IRiotPlatformClient _platformClient = Substitute.For<IRiotPlatformClient>();
    private readonly IDataSessionFactory _sessionFactory = Substitute.For<IDataSessionFactory>();
    private readonly CapturingLogger<ManualSeedProcess> _logger = new();

    public ManualSeedProcessWrongPlatformTests()
    {
        _store.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SeedRequestDocument>>([_request]));
        _store.ClaimAsync(_request.Id, Arg.Any<CancellationToken>()).Returns(true);
        _store.GetByIdAsync(_request.Id, Arg.Any<CancellationToken>()).Returns(_request);
    }

    [Fact]
    public async Task RunCoreAsync_WhenSummonerV4Returns404_FailsTheRequestAsNoSummonerOnPlatform()
    {
        _platformClient.GetSummonerByPuuidAsync(PlatformRoute.BR1, "puuid-1", Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Response status code does not indicate success: 404 (Not Found).", null, HttpStatusCode.NotFound));

        var summary = await BuildProcess().RunCoreAsync(CancellationToken.None);

        await _store.Received(1).MarkFailedAsync(
            _request.Id,
            "Account exists on Americas but has no summoner on BR1",
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());

        var entry = _logger.Entries.Should()
            .ContainSingle(e => e.EventId.Id == OpsEvents.SeedRequestFailed.Id).Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Exception.Should().BeNull();
        entry.Message.Should().Contain("has no summoner on BR1");

        summary.Should().BeOfType<ManualSeedSummary>()
            .Which.Should().Be(new ManualSeedSummary(
                Claimed: 1, Ingested: 0, NotFound: 0, Failed: 0, CandidatesQueued: 0, NoSummonerOnPlatform: 1));

        // Nothing to write for an account that does not exist on this platform.
        await _sessionFactory.DidNotReceive().CreateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCoreAsync_WhenSummonerV4FailsOtherwise_KeepsTheGenericFailurePath()
    {
        var failure = new HttpRequestException("Response status code does not indicate success: 503 (Service Unavailable).", null, HttpStatusCode.ServiceUnavailable);
        _platformClient.GetSummonerByPuuidAsync(PlatformRoute.BR1, "puuid-1", Arg.Any<CancellationToken>())
            .ThrowsAsync(failure);

        var summary = await BuildProcess().RunCoreAsync(CancellationToken.None);

        var entry = _logger.Entries.Should()
            .ContainSingle(e => e.EventId.Id == OpsEvents.SeedRequestFailed.Id).Subject;
        entry.Exception.Should().BeSameAs(failure);
        summary.Should().BeOfType<ManualSeedSummary>()
            .Which.Should().Be(new ManualSeedSummary(1, 0, 0, 1, 0, NoSummonerOnPlatform: 0));
    }

    private ManualSeedProcess BuildProcess()
    {
        var accountClient = Substitute.For<IRiotAccountClient>();
        accountClient.GetByRiotIdAsync("Faker", "CN1", RegionalRoute.Americas, Arg.Any<CancellationToken>())
            .Returns(new RiotAccountDto { Puuid = "puuid-1", GameName = "Faker", TagLine = "CN1" });

        return new ManualSeedProcess(
            _logger,
            accountClient,
            _platformClient,
            _sessionFactory,
            _store,
            Substitute.For<IAccountUpsertService>(),
            Substitute.For<ICandidateUpsertService>(),
            Substitute.For<IAuditLog>(),
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new ManualSeedOptions()));
    }
}
