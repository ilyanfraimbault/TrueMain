using AwesomeAssertions;
using Data;
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
public sealed class MatchTeamPositionCorrectionProcessIntegrationTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;

    public MatchTeamPositionCorrectionProcessIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task RunAsync_ResolvesTheOneUnresolvedMember_OnAFullTeamMissingOneLane()
    {
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

    [Fact]
    public async Task CandidateScan_ReadsThePendingPartialIndex_NotTheTable()
    {
        // A table where unresolved lanes are the rare exception, as in production: 300
        // complete matches and three teams with a blank lane. Planner statistics come
        // from VACUUM ANALYZE, so the plan below is the one the cost model actually picks
        // — no enable_* switch forces it (#2095).
        await using (var db = _fixture.CreateDbContext())
        {
            string[] lanes = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];
            for (var match = 0; match < 300; match++)
            {
                var blankParticipant = match % 100 == 0 ? 3 : 0;
                AddMatch(
                    db,
                    $"m-plan-{match:D4}",
                    Enumerable.Range(1, 10)
                        .Select(id => (id, id <= 5 ? 100 : 200, id == blankParticipant ? "" : lanes[(id - 1) % 5]))
                        .ToArray());
            }

            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("VACUUM ANALYZE match_participants;");
        }

        string plan;
        await using (var db = _fixture.CreateDbContext())
        {
            // The command EF itself sends, parameters included, so the test cannot drift
            // from the query the process runs.
            await using var command = MatchTeamPositionCorrectionProcess.CandidateTeamsQuery(db).CreateDbCommand();
            command.CommandText = "EXPLAIN " + command.CommandText;
            await db.Database.OpenConnectionAsync();
            await using var reader = await command.ExecuteReaderAsync();
            var lines = new List<string>();
            while (await reader.ReadAsync())
            {
                lines.Add(reader.GetString(0));
            }
            plan = string.Join('\n', lines);
        }

        plan.Should().Contain("IX_match_participants_team_position_pending");
        plan.Should().NotContain("Seq Scan");
        plan.Should().NotContain("IX_match_participants_champion_position_full");
    }

    private async Task<string> SeedMatchAsync(string matchId, params (int ParticipantId, int TeamId, string TeamPosition)[] participants)
    {
        await using var db = _fixture.CreateDbContext();
        AddMatch(db, matchId, participants);
        await db.SaveChangesAsync();
        return matchId;
    }

    private static void AddMatch(TrueMainDbContext db, string matchId, (int ParticipantId, int TeamId, string TeamPosition)[] participants)
    {
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
