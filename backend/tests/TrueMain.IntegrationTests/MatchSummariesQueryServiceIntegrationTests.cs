using AwesomeAssertions;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.Services.Truemains.Identity;
using TrueMain.Services.Truemains.Leaderboard;
using TrueMain.Services.Truemains.Matches;
using TrueMain.Services.Truemains.PlayerChampions;
using TrueMain.Services.Truemains.Profile;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Covers the paging-default consistency fixed by #222: an out-of-range page
/// must report the same clamped <c>Page</c> whether the account has zero
/// matching matches (short-circuits before pagination) or a page past a
/// positive total (falls through the normal query path).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class MatchSummariesQueryServiceIntegrationTests : IAsyncLifetime
{
    private const string NameTag = "TestSummoner-KR1";

    private readonly PostgresFixture _fixture;

    public MatchSummariesQueryServiceIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task GetAsync_ZeroMatches_ReturnsTheRequestedClampedPage()
    {
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
        await SeedAccountAsync();
        await SeedMatchAsync("MATCH_ONE");

        await using var db = _fixture.CreateDbContext();
        var response = await CreateService(db).GetAsync(
            NameTag, page: 3, pageSize: 20, position: null, championId: null, CancellationToken.None);

        response.Should().NotBeNull();
        response!.Total.Should().Be(1);
        response.Page.Should().Be(3, "matches the zero-total case so pagination never disagrees about the page shown");
    }

    [Fact]
    public async Task GetAsync_MatchesSharingAStartTime_PageInAStableIdOrder()
    {
        await SeedAccountAsync();
        var startedAt = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        foreach (var matchId in new[] { "MATCH_B", "MATCH_C", "MATCH_A" })
        {
            await SeedMatchAsync(matchId, startedAt);
        }

        var seen = new List<string>();
        for (var page = 1; page <= 3; page++)
        {
            await using var db = _fixture.CreateDbContext();
            var response = await CreateService(db).GetAsync(
                NameTag, page, pageSize: 1, position: null, championId: null, CancellationToken.None);
            seen.AddRange(response!.Matches.Select(m => m.MatchId));
        }

        seen.Should().Equal(
            ["MATCH_C", "MATCH_B", "MATCH_A"],
            "a start-time tie is broken by match id, so offset pages never repeat or skip a match (#1635)");
    }

    [Fact]
    public async Task GetAsync_ListsTheGamesPastRetentionAfterTheLiveOnes_AcrossPages()
    {
        // #2358: two live games, then two kept as truncated seats. Another player's seat in the
        // same match, and a filter on another champion, stay out.
        await SeedAccountAsync();
        await SeedMatchAsync("LIVE_NEW", new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc));
        await SeedMatchAsync("LIVE_OLD", new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc));
        await using (var seed = _fixture.CreateDbContext())
        {
            var accountId = await seed.RiotAccounts.Where(a => a.Puuid == "puuid-match-summaries-paging").Select(a => a.Id).SingleAsync();
            seed.TruncatedMatchSeats.AddRange(
                TruncatedSeat("OLD_1", 1, accountId, 157, new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc)),
                TruncatedSeat("OLD_1", 6, null, 238, new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc)),
                TruncatedSeat("OLD_2", 1, accountId, 103, new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc)));
            await seed.SaveChangesAsync();
        }

        var seen = new List<(string MatchId, bool IsTruncated)>();
        int? total = null;
        for (var page = 1; page <= 2; page++)
        {
            await using var db = _fixture.CreateDbContext();
            var response = await CreateService(db).GetAsync(
                NameTag, page, pageSize: 3, position: null, championId: null, CancellationToken.None);
            total = response!.Total;
            seen.AddRange(response.Matches.Select(m => (m.MatchId, m.IsTruncated)));
        }

        total.Should().Be(4);
        seen.Should().Equal(("LIVE_NEW", false), ("LIVE_OLD", false), ("OLD_1", true), ("OLD_2", true));

        await using var filterDb = _fixture.CreateDbContext();
        var filtered = await CreateService(filterDb).GetAsync(
            NameTag, 1, pageSize: 20, position: null, championId: 103, CancellationToken.None);
        filtered!.Matches.Select(m => m.MatchId).Should().Equal("OLD_2");
    }

    private static TruncatedMatchSeat TruncatedSeat(string matchId, int participantId, Guid? accountId, int championId, DateTime startedAt)
        => new()
        {
            MatchId = matchId,
            ParticipantId = participantId,
            PlatformId = "KR",
            QueueId = 420,
            Patch = "16.18",
            GameStartTimeUtc = startedAt,
            GameDurationSeconds = 1800,
            EloBracket = "MASTER",
            TeamId = participantId <= 5 ? 100 : 200,
            Win = true,
            ChampionId = championId,
            TeamPosition = "MIDDLE",
            RiotAccountId = accountId,
            SkillOrderKey = "Q-W-E",
        };

    private static MatchSummariesQueryService CreateService(Data.TrueMainDbContext db)
        => new(
            db,
            new TruemainAccountResolver(db),
            new MatchSummaryHydrator(db, NullLogger<MatchSummaryHydrator>.Instance),
            NullLogger<MatchSummariesQueryService>.Instance);

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

    private async Task SeedMatchAsync(string matchId, DateTime? gameStartTimeUtc = null)
    {
        await using var db = _fixture.CreateDbContext();
        var match = new MatchBuilder().WithId(matchId);
        if (gameStartTimeUtc is { } startedAt)
        {
            match = match.WithGameStartTimeUtc(startedAt);
        }

        db.Matches.Add(match.Build());
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
