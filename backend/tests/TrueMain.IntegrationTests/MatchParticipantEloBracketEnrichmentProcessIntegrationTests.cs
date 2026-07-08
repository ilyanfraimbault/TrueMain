using AwesomeAssertions;
using Core.Lol.Ranking;
using Data.Entities;
using Ingestor.Processes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Covers the <see cref="MatchParticipantEloBracketEnrichmentProcess"/> that stamps
/// <c>match_participants.elo_bracket</c> — the column the six live champion-page
/// panels filter on. The behaviour under test is the self-healing contract: a game
/// whose account has no rank snapshot yet is left unenriched (not stamped UNRANKED)
/// so a later cycle reclassifies it once the snapshot arrives, exactly like the
/// aggregation that recomputes the band every cycle.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class MatchParticipantEloBracketEnrichmentProcessIntegrationTests
{
    private const int QueueId = 420;
    private const int Champion = 157; // Yone
    private const string Position = "MIDDLE";

    private readonly PostgresFixture _fixture;

    public MatchParticipantEloBracketEnrichmentProcessIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunAsync_StampsEachTrackedRow_WithNearestSnapshotBand()
    {
        await _fixture.ResetDatabaseAsync();

        var silverGameStart = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        var masterGameStart = new DateTime(2026, 2, 20, 12, 0, 0, DateTimeKind.Utc);

        var account = await SeedAccountAsync("enrich-nearest", "enrich-nearest-puuid");
        var silverMatchId = await AddGameAsync("m-silver", silverGameStart, account.Id);
        var masterMatchId = await AddGameAsync("m-master", masterGameStart, account.Id);

        await SeedSnapshotsAsync(
            (account.Id, silverGameStart, "SILVER"),
            (account.Id, masterGameStart, "MASTER"));

        await RunEnrichmentAsync();

        var bands = await BandsByMatchAsync();
        // Each game buckets by the snapshot captured nearest its start.
        bands[silverMatchId].Should().Be(EloBracket.Silver);
        bands[masterMatchId].Should().Be(EloBracket.Master);
    }

    [Fact]
    public async Task RunAsync_DefersRowWithoutSnapshot_ThenSelfHealsOnceSnapshotArrives()
    {
        await _fixture.ResetDatabaseAsync();

        var gameStart = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var account = await SeedAccountAsync("enrich-defer", "enrich-defer-puuid");
        var matchId = await AddGameAsync("m-defer", gameStart, account.Id);

        // First cycle: the account has no snapshot yet, so the row must be left
        // unenriched (empty) — NOT stamped UNRANKED, which would be permanent and
        // diverge from builds/tierlist for the same game.
        await RunEnrichmentAsync();
        (await BandsByMatchAsync())[matchId].Should().BeEmpty(
            "a game ingested before its account's first rank sync stays unenriched, not UNRANKED");

        // The snapshot arrives (a later AccountRefresh), then the next cycle stamps
        // the row with its real band — the row self-heals rather than staying stuck.
        await SeedSnapshotsAsync((account.Id, gameStart, "MASTER"));
        await RunEnrichmentAsync();
        (await BandsByMatchAsync())[matchId].Should().Be(EloBracket.Master);
    }

    [Fact]
    public async Task RunAsync_StampsGenuineUnranked_WhenSnapshotHasNoTier()
    {
        await _fixture.ResetDatabaseAsync();

        var gameStart = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
        var account = await SeedAccountAsync("enrich-unranked", "enrich-unranked-puuid");
        var matchId = await AddGameAsync("m-unranked", gameStart, account.Id);

        // A snapshot exists but carries no ranked tier → the band resolves to a
        // genuine, final UNRANKED (as opposed to the deferred empty above).
        await SeedSnapshotsAsync((account.Id, gameStart, ""));

        await RunEnrichmentAsync();
        (await BandsByMatchAsync())[matchId].Should().Be(EloBracket.Unranked);
    }

    [Fact]
    public async Task RunAsync_LeavesUntrackedRowsUntouched()
    {
        await _fixture.ResetDatabaseAsync();

        var gameStart = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        // No RiotAccountId → an anonymous participant that merely shared a tracked
        // player's game. It is never a source row for any panel, so it is skipped.
        var matchId = await AddGameAsync("m-anon", gameStart, riotAccountId: null);

        await RunEnrichmentAsync();
        (await BandsByMatchAsync())[matchId].Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_DoesNotRestampAlreadyEnrichedRows()
    {
        await _fixture.ResetDatabaseAsync();

        var gameStart = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var account = await SeedAccountAsync("enrich-stable", "enrich-stable-puuid");
        // Pre-stamped GOLD, with a snapshot that would resolve to MASTER. The
        // stamp-once contract means the enriched row keeps its band: only the
        // still-empty set is ever written.
        var matchId = await AddGameAsync("m-stable", gameStart, account.Id, eloBracket: EloBracket.Gold);
        await SeedSnapshotsAsync((account.Id, gameStart, "MASTER"));

        await RunEnrichmentAsync();
        (await BandsByMatchAsync())[matchId].Should().Be(EloBracket.Gold);
    }

    private async Task<RiotAccount> SeedAccountAsync(string gameName, string puuid)
    {
        await using var db = _fixture.CreateDbContext();
        var account = new RiotAccountBuilder()
            .WithGameName(gameName)
            .WithTagLine("KR1")
            .WithPuuid(puuid)
            .Build();
        db.RiotAccounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    private async Task<string> AddGameAsync(
        string matchId, DateTime gameStart, Guid? riotAccountId, string? eloBracket = null)
    {
        await using var db = _fixture.CreateDbContext();
        db.Matches.Add(new MatchBuilder()
            .WithId(matchId)
            .WithQueueId(QueueId)
            .WithGameStartTimeUtc(gameStart)
            .Build());

        db.MatchParticipants.Add(new MatchParticipant
        {
            MatchId = matchId,
            ParticipantId = 1,
            Puuid = $"puuid-{matchId}",
            RiotAccountId = riotAccountId,
            SummonerName = "seed",
            SummonerLevel = 100,
            ChampionId = Champion,
            TeamId = 100,
            TeamPosition = Position,
            IndividualPosition = Position,
            Lane = Position,
            Role = "SOLO",
            Win = true,
            ChampLevel = 16,
            Item6 = 3363,
            TrinketItemId = 3363,
            EloBracket = eloBracket ?? string.Empty,
            ItemEvents = [],
            SkillEvents = []
        });

        await db.SaveChangesAsync();
        return matchId;
    }

    private async Task SeedSnapshotsAsync(params (Guid AccountId, DateTime CapturedAtUtc, string Tier)[] snapshots)
    {
        await using var db = _fixture.CreateDbContext();
        db.RankSnapshots.AddRange(snapshots.Select(snapshot => new RankSnapshot
        {
            RiotAccountId = snapshot.AccountId,
            CapturedAtUtc = snapshot.CapturedAtUtc,
            Tier = snapshot.Tier,
            Division = "I",
            LeaguePoints = 50
        }));
        await db.SaveChangesAsync();
    }

    private async Task<Dictionary<string, string>> BandsByMatchAsync()
    {
        await using var db = _fixture.CreateDbContext();
        return await db.MatchParticipants
            .AsNoTracking()
            .ToDictionaryAsync(participant => participant.MatchId, participant => participant.EloBracket);
    }

    private async Task RunEnrichmentAsync()
    {
        var process = new MatchParticipantEloBracketEnrichmentProcess(
            NullLogger<MatchParticipantEloBracketEnrichmentProcess>.Instance,
            new TestDbContextFactory(_fixture));
        await process.RunCoreAsync(CancellationToken.None);
    }
}
