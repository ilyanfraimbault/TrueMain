using AwesomeAssertions;
using Data.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.Services.Truemains;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Covers the paging-default consistency fixed by #222: an out-of-range page
/// must report the same clamped <c>Page</c> whether the account has zero
/// matching matches (short-circuits before pagination) or a page past a
/// positive total (falls through the normal query path).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class MatchSummariesQueryServiceIntegrationTests
{
    private const string NameTag = "TestSummoner-KR1";

    private readonly PostgresFixture _fixture;

    public MatchSummariesQueryServiceIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetAsync_ZeroMatches_ReturnsTheRequestedClampedPage()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAccountAsync();

        await using var db = _fixture.CreateDbContext();
        var response = await CreateService(db).GetAsync(
            NameTag, page: 3, pageSize: 20, position: null, championId: null, CancellationToken.None);

        response.Should().NotBeNull();
        response!.Total.Should().Be(0);
        response.Page.Should().Be(3, "an empty result must report the same clamped page an out-of-range one does");
    }

    [Fact]
    public async Task GetAsync_PageBeyondPositiveTotal_ReturnsTheRequestedClampedPage()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAccountAsync();
        await SeedMatchAsync("MATCH_ONE");

        await using var db = _fixture.CreateDbContext();
        var response = await CreateService(db).GetAsync(
            NameTag, page: 3, pageSize: 20, position: null, championId: null, CancellationToken.None);

        response.Should().NotBeNull();
        response!.Total.Should().Be(1);
        response.Page.Should().Be(3, "matches the zero-total case so pagination never disagrees about the page shown");
    }

    private static MatchSummariesQueryService CreateService(Data.TrueMainDbContext db)
        => new(db, NullLogger<MatchSummariesQueryService>.Instance);

    private async Task SeedAccountAsync()
    {
        await using var db = _fixture.CreateDbContext();
        db.RiotAccounts.Add(new RiotAccountBuilder()
            .WithGameName("TestSummoner")
            .WithTagLine("KR1")
            .WithPuuid("puuid-match-summaries-paging")
            .Build());
        await db.SaveChangesAsync();
    }

    private async Task SeedMatchAsync(string matchId)
    {
        await using var db = _fixture.CreateDbContext();
        db.Matches.Add(new MatchBuilder().WithId(matchId).Build());
        db.MatchParticipants.Add(new MatchParticipant
        {
            MatchId = matchId,
            ParticipantId = 1,
            Puuid = "puuid-match-summaries-paging",
            RiotAccountId = null,
            SummonerName = "seed",
            SummonerLevel = 100,
            ChampionId = 157,
            TeamId = 100,
            TeamPosition = "MIDDLE",
            IndividualPosition = "MIDDLE",
            Lane = "MIDDLE",
            Role = "SOLO",
            Win = true,
            Kills = 5,
            Deaths = 4,
            Assists = 6,
            GoldEarned = 12000,
            TotalMinionsKilled = 180,
            NeutralMinionsKilled = 4,
            ChampLevel = 16,
            Item0 = 3153,
            Item1 = 3006,
            Item2 = 3031,
            Item3 = 0,
            Item4 = 0,
            Item5 = 0,
            Item6 = 3363,
            TrinketItemId = 3363,
            PerksDefense = 5001,
            PerksFlex = 5008,
            PerksOffense = 5005,
            PrimaryStyleId = 8000,
            SubStyleId = 8100,
            Summoner1Id = 4,
            Summoner2Id = 12,
            EloBracket = "",
            ItemEvents = [],
            SkillEvents = []
        });
        await db.SaveChangesAsync();
    }
}
