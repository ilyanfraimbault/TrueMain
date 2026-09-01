using System.Text.Json;
using AwesomeAssertions;
using Core.Lol.Identifiers;
using Data.Entities;
using Data.Repositories;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// Riot sometimes cuts a timeline body short: the response is a 200 whose payload
/// dies mid-stream, so the resilience handler (which decides on the headers) never
/// retries it and System.Text.Json throws while reading. One such payload used to
/// abort the whole account's ingestion — see issue #1052. These tests pin the
/// boundary between "isolated bad payload" and "systemic failure"; since #1229 that
/// boundary lives in the download phase, which runs before the write transaction is
/// opened, so a systemic failure aborts the account without ever having written.
/// </summary>
public sealed class TimelineIngestionServiceFailureIsolationTests
{
    [Fact]
    public async Task IngestTimelinesAsync_SkipsTruncatedTimelinesBelowTheConsecutiveFailureCap()
    {
        var matchIds = BuildMatchIds(TimelineIngestionService.MaxConsecutiveTimelineFailures - 1);
        var session = BuildSession();
        var service = new TimelineIngestionService(
            new TruncatingRiotMatchClient(matchIds),
            NullLogger<TimelineIngestionService>.Instance);

        var plan = await service.PrepareAsync(
            session,
            RegionalRoute.Asia,
            Array.Empty<string>(),
            matchIds,
            CancellationToken.None);

        plan.Timelines.Should().BeEmpty();

        var updated = await service.WriteAsync(session, plan, saveBatchSize: 10, CancellationToken.None);

        updated.Should().Be(0);
        // Nothing was marked ingested, so the pending-timeline path re-fetches these
        // matches on a later run instead of the account being reverted to queued.
        await session.Matches
            .DidNotReceive()
            .SetTimelineIngestedAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestTimelinesAsync_RethrowsOnceFailuresLookSystemic()
    {
        var matchIds = BuildMatchIds(TimelineIngestionService.MaxConsecutiveTimelineFailures);
        var session = BuildSession();
        var service = new TimelineIngestionService(
            new TruncatingRiotMatchClient(matchIds),
            NullLogger<TimelineIngestionService>.Instance);

        var ingest = async () => await service.PrepareAsync(
            session,
            RegionalRoute.Asia,
            Array.Empty<string>(),
            matchIds,
            CancellationToken.None);

        // A Riot outage must still abort the account rather than report a healthy
        // run that quietly ingested nothing.
        await ingest.Should().ThrowAsync<JsonException>();
    }

    [Fact]
    public async Task IngestTimelinesAsync_ResetsTheFailureCountAfterASuccessfulTimeline()
    {
        // Failures either side of a timeline that downloaded fine are not
        // consecutive, so they must not add up to the cap.
        var belowCap = TimelineIngestionService.MaxConsecutiveTimelineFailures - 1;
        var matchIds = BuildMatchIds((belowCap * 2) + 1);
        var healthyMatchId = matchIds[belowCap];
        var truncated = matchIds.Where(id => id != healthyMatchId).ToArray();

        var session = BuildSession();
        var service = new TimelineIngestionService(
            new TruncatingRiotMatchClient(truncated),
            NullLogger<TimelineIngestionService>.Instance);

        var ingest = async () => await service.PrepareAsync(
            session,
            RegionalRoute.Asia,
            Array.Empty<string>(),
            matchIds,
            CancellationToken.None);

        await ingest.Should().NotThrowAsync();
    }

    private static string[] BuildMatchIds(int count)
        => Enumerable.Range(0, count).Select(i => $"KR_{i:D3}").ToArray();

    private static IDataSession BuildSession()
    {
        var matches = Substitute.For<IMatchRepository>();
        // The ids under test are passed as newMatchIds so the service iterates them
        // in a deterministic order (a pending HashSet would not guarantee one).
        matches
            .GetTimelinePendingMatchIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(StringComparer.Ordinal));

        var participants = Substitute.For<IMatchParticipantRepository>();
        participants
            .GetByMatchIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<MatchParticipant>());

        var session = Substitute.For<IDataSession>();
        session.Matches.Returns(matches);
        session.MatchParticipants.Returns(participants);
        return session;
    }

    private sealed class TruncatingRiotMatchClient(IReadOnlyCollection<string> truncatedMatchIds) : IRiotMatchClient
    {
        public Task<RiotMatchDto> GetMatchAsync(string matchId, RegionalRoute region, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<List<string>> GetMatchIdsAsync(string puuid, RegionalRoute region, int count, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<MatchTimelineDto> GetTimelineAsync(string matchId, RegionalRoute region, CancellationToken ct)
        {
            if (truncatedMatchIds.Contains(matchId, StringComparer.Ordinal))
            {
                throw new JsonException("Expected a value, but instead reached end of data.");
            }

            return Task.FromResult(new MatchTimelineDto { Events = [] });
        }
    }
}
