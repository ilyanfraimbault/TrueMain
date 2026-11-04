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
/// #2448: a counter upsert holds its rows until the account's transaction commits. One upsert
/// per save batch meant a transaction locked the hot rows in several passes and kept them
/// through the rest of the account's writes, so ordering each statement could not make two
/// transactions agree on an order. The whole plan folds once, last.
/// </summary>
public sealed class TimelineIngestionServiceCounterFoldTests
{
    [Fact]
    public async Task WriteAsync_UpsertsTheCountersOnce_AfterTheLastSave_WhateverTheBatchCount()
    {
        var session = BuildSession();
        var service = new TimelineIngestionService(
            Substitute.For<IRiotMatchClient>(),
            NullLogger<TimelineIngestionService>.Instance);

        var plan = await PrepareAsync(session, "KR_1", "KR_2", "KR_3");

        var updated = await service.WriteAsync(session, plan, saveBatchSize: 1, CancellationToken.None);

        updated.Should().Be(3);
        await session.PaceBenchmarkStats.Received(1).AddCountsAsync(
            Arg.Any<IReadOnlyCollection<PaceBenchmarkCount>>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await session.FormBenchmarkStats.Received(1).AddCountsAsync(
            Arg.Any<IReadOnlyCollection<FormBenchmarkCount>>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());

        Received.InOrder(() =>
        {
            _ = session.SaveChangesAsync(Arg.Any<CancellationToken>());
            _ = session.SaveChangesAsync(Arg.Any<CancellationToken>());
            _ = session.SaveChangesAsync(Arg.Any<CancellationToken>());
            _ = session.PaceBenchmarkStats.AddCountsAsync(
                Arg.Any<IReadOnlyCollection<PaceBenchmarkCount>>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
            _ = session.FormBenchmarkStats.AddCountsAsync(
                Arg.Any<IReadOnlyCollection<FormBenchmarkCount>>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        });
    }

    private static async Task<TimelineIngestionPlan> PrepareAsync(IDataSession session, params string[] matchIds)
    {
        var client = Substitute.For<IRiotMatchClient>();
        client
            .GetTimelineAsync(Arg.Any<string>(), Arg.Any<RegionalRoute>(), Arg.Any<CancellationToken>())
            .Returns(new MatchTimelineDto());

        var preparing = new TimelineIngestionService(client, NullLogger<TimelineIngestionService>.Instance);
        return await preparing.PrepareAsync(session, RegionalRoute.Asia, [], matchIds, CancellationToken.None);
    }

    private static IDataSession BuildSession()
    {
        var matches = Substitute.For<IMatchRepository>();
        matches
            .GetTimelinePendingMatchIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(StringComparer.Ordinal));
        matches
            .ClaimPaceBenchmarkFoldAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<string>>()
                .Select(id => new PaceBenchmarkFoldClaim(id, "16.19", DateTime.UtcNow, 1500, EndedInEarlySurrender: false))
                .ToList());

        var participants = Substitute.For<IMatchParticipantRepository>();
        participants
            .GetByMatchIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new List<MatchParticipant>
            {
                new()
                {
                    MatchId = call.Arg<string>(),
                    ParticipantId = 1,
                    Puuid = "puuid",
                    TeamPosition = "TOP",
                    IndividualPosition = "TOP",
                    ItemEvents = [],
                    SkillEvents = []
                }
            });

        var session = Substitute.For<IDataSession>();
        session.Matches.Returns(matches);
        session.MatchParticipants.Returns(participants);
        return session;
    }
}
