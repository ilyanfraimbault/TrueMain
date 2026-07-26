using AwesomeAssertions;
using Data.Entities;
using Ingestor.Processes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Covers the <see cref="MatchTeamPositionCorrectionProcess"/> backfill that
/// resolves the unambiguous "Missing team position" shape already sitting in the
/// data-quality backlog: a full 5-player team missing exactly one canonical lane,
/// with exactly one member whose <c>TeamPosition</c> is blank. Anything more
/// ambiguous must stay untouched — it's still surfaced on the admin panel for
/// manual review.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class MatchTeamPositionCorrectionProcessIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public MatchTeamPositionCorrectionProcessIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunAsync_ResolvesTheOneUnresolvedMember_OnAFullTeamMissingOneLane()
    {
        await _fixture.ResetDatabaseAsync();

        var matchId = await SeedMatchAsync(
            "m-resolvable",
            (1, 100, "TOP"),
            (2, 100, "JUNGLE"),
            (3, 100, ""), // missing MIDDLE, only unresolved member
            (4, 100, "BOTTOM"),
            (5, 100, "UTILITY"));

        await RunCorrectionAsync();

        var positions = await PositionsByParticipantAsync(matchId);
        positions[3].Should().Be("MIDDLE");
    }

    [Fact]
    public async Task RunAsync_LeavesTeamUntouched_WhenMoreThanOneMemberIsUnresolved()
    {
        await _fixture.ResetDatabaseAsync();

        var matchId = await SeedMatchAsync(
            "m-ambiguous",
            (1, 100, "TOP"),
            (2, 100, "JUNGLE"),
            (3, 100, ""), // two gaps (MIDDLE, UTILITY), two unresolved members
            (4, 100, "BOTTOM"),
            (5, 100, ""));

        await RunCorrectionAsync();

        var positions = await PositionsByParticipantAsync(matchId);
        positions[3].Should().BeEmpty();
        positions[5].Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_LeavesTeamUntouched_WhenHeadcountIsNotFive()
    {
        await _fixture.ResetDatabaseAsync();

        var matchId = await SeedMatchAsync(
            "m-shortcount",
            (1, 100, "TOP"),
            (2, 100, "JUNGLE"),
            (3, 100, ""),
            (4, 100, "BOTTOM"));

        await RunCorrectionAsync();

        var positions = await PositionsByParticipantAsync(matchId);
        positions[3].Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ResolvesBothTeams_InTheSameMatch()
    {
        await _fixture.ResetDatabaseAsync();

        var matchId = await SeedMatchAsync(
            "m-both-teams",
            (1, 100, "TOP"),
            (2, 100, "JUNGLE"),
            (3, 100, "MIDDLE"),
            (4, 100, "BOTTOM"),
            (5, 100, ""), // team 100 missing UTILITY
            (6, 200, ""), // team 200 missing TOP
            (7, 200, "JUNGLE"),
            (8, 200, "MIDDLE"),
            (9, 200, "BOTTOM"),
            (10, 200, "UTILITY"));

        await RunCorrectionAsync();

        var positions = await PositionsByParticipantAsync(matchId);
        positions[5].Should().Be("UTILITY");
        positions[6].Should().Be("TOP");
    }

    private async Task<string> SeedMatchAsync(string matchId, params (int ParticipantId, int TeamId, string TeamPosition)[] participants)
    {
        await using var db = _fixture.CreateDbContext();
        db.Matches.Add(new MatchBuilder().WithId(matchId).Build());

        foreach (var (participantId, teamId, teamPosition) in participants)
        {
            db.MatchParticipants.Add(new MatchParticipant
            {
                MatchId = matchId,
                ParticipantId = participantId,
                Puuid = $"puuid-{matchId}-{participantId}",
                SummonerName = "seed",
                SummonerLevel = 100,
                ChampionId = 157,
                TeamId = teamId,
                TeamPosition = teamPosition,
                IndividualPosition = teamPosition,
                Lane = teamPosition,
                Role = "SOLO",
                Win = teamId == 100,
                ChampLevel = 16,
                ItemEvents = [],
                SkillEvents = []
            });
        }

        await db.SaveChangesAsync();
        return matchId;
    }

    private async Task<Dictionary<int, string>> PositionsByParticipantAsync(string matchId)
    {
        await using var db = _fixture.CreateDbContext();
        return await db.MatchParticipants
            .AsNoTracking()
            .Where(p => p.MatchId == matchId)
            .ToDictionaryAsync(p => p.ParticipantId, p => p.TeamPosition);
    }

    private async Task RunCorrectionAsync()
    {
        var process = new MatchTeamPositionCorrectionProcess(
            NullLogger<MatchTeamPositionCorrectionProcess>.Instance,
            new TestDbContextFactory(_fixture));
        await process.RunCoreAsync(CancellationToken.None);
    }
}
