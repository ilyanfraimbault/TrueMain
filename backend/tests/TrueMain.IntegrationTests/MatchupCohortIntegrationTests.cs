using AwesomeAssertions;
using Data;
using Data.Aggregation;
using Data.Entities;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Pins <see cref="MatchupCohort"/> directly, one clause of its predicate at a time.
/// Three folds read this set and the champion aggregates the matchups panel is read
/// beside depend on it agreeing with them; when it last drifted (#1087) it put 3.2×
/// more games behind the panel than behind the header immediately above it, and the
/// fold suites that exercise it end-to-end only caught that because someone compared
/// the two numbers by eye.
/// </summary>
/// <remarks>
/// It needs a real Postgres rather than a unit test: the whole rule is a three-way
/// join whose key — (platform, puuid, champion) — is the part that can silently stop
/// matching.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class MatchupCohortIntegrationTests
{
    private const int QueueId = 420;
    private const int Yone = 157;
    private const int Zed = 238;
    private const string Platform = "EUW1";
    private const string TrackedPuuid = "cohort-main-puuid";

    private readonly PostgresFixture _fixture;

    public MatchupCohortIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Returns_an_empty_set_when_no_matches_are_asked_for()
    {
        await _fixture.ResetDatabaseAsync();
        await using var db = _fixture.CreateDbContext();

        var keys = await MatchupCohort.LoadAsync(db, [], CancellationToken.None);

        keys.Should().BeEmpty();
    }

    [Fact]
    public async Task Admits_a_tracked_participant_that_mains_the_champion_it_played()
    {
        await _fixture.ResetDatabaseAsync();

        await using (var db = _fixture.CreateDbContext())
        {
            var account = AddAccount(db, TrackedPuuid);
            Seed(db, "EUW1_MAIN", TrackedPuuid, Yone, account.Id, participantId: 4);
            db.MainChampionStats.Add(MainRow(TrackedPuuid, Yone, isMain: true));
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var keys = await MatchupCohort.LoadAsync(db, ["EUW1_MAIN"], CancellationToken.None);

            keys.Should().ContainSingle()
                .Which.Should().Be(new MatchupCohortKey("EUW1_MAIN", 4));
        }
    }

    [Fact]
    public async Task Excludes_an_untracked_participant_even_when_a_main_row_exists_for_its_puuid()
    {
        await _fixture.ResetDatabaseAsync();

        await using (var db = _fixture.CreateDbContext())
        {
            // No RiotAccountId: somebody we happened to see in a game, not an account we
            // follow. The main row is keyed on (platform, puuid, champion) and joins
            // happily — the account clause is the only thing keeping this row out.
            Seed(db, "EUW1_ORPHAN", "orphan-puuid", Yone, riotAccountId: null, participantId: 1);
            db.MainChampionStats.Add(MainRow("orphan-puuid", Yone, isMain: true));
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var keys = await MatchupCohort.LoadAsync(db, ["EUW1_ORPHAN"], CancellationToken.None);

            keys.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Excludes_a_tracked_participant_playing_a_champion_it_does_not_main()
    {
        await _fixture.ResetDatabaseAsync();

        await using (var db = _fixture.CreateDbContext())
        {
            var account = AddAccount(db, TrackedPuuid);

            // The off-main game: the account is known, and even has a row for this
            // champion, but IsMain is false. Gating on "an account we know" instead is
            // exactly the regression of #1087.
            Seed(db, "EUW1_OFFMAIN", TrackedPuuid, Zed, account.Id, participantId: 1);
            db.MainChampionStats.Add(MainRow(TrackedPuuid, Zed, isMain: false));

            // …and the champion it does main, in another game, so the gate is shown to be
            // per (puuid, champion) rather than per account.
            Seed(db, "EUW1_ONMAIN", TrackedPuuid, Yone, account.Id, participantId: 1);
            db.MainChampionStats.Add(MainRow(TrackedPuuid, Yone, isMain: true));

            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var keys = await MatchupCohort.LoadAsync(
                db, ["EUW1_OFFMAIN", "EUW1_ONMAIN"], CancellationToken.None);

            keys.Select(key => key.MatchId).Should().BeEquivalentTo(["EUW1_ONMAIN"]);
        }
    }

    [Fact]
    public async Task Excludes_a_participant_whose_main_row_was_computed_on_another_platform()
    {
        await _fixture.ResetDatabaseAsync();

        await using (var db = _fixture.CreateDbContext())
        {
            var account = AddAccount(db, "traveller-puuid");
            Seed(db, "EUW1_XPLAT", "traveller-puuid", Yone, account.Id, participantId: 2);
            // Same puuid, same champion, other region. The join carries the platform — of
            // the match, not of the account — precisely so a main computed on one server
            // cannot vouch for games played on another.
            db.MainChampionStats.Add(MainRow("traveller-puuid", Yone, isMain: true, platform: "NA1"));
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var keys = await MatchupCohort.LoadAsync(db, ["EUW1_XPLAT"], CancellationToken.None);

            keys.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Keeps_a_retired_main_because_IsActive_only_retires_future_ingestion()
    {
        await _fixture.ResetDatabaseAsync();

        await using (var db = _fixture.CreateDbContext())
        {
            var account = AddAccount(db, "retired-puuid");
            Seed(db, "EUW1_RETIRED", "retired-puuid", Yone, account.Id, participantId: 6);
            // IsActive=false says the player stopped playing this champion (#900). Testing
            // it here would silently drop already-folded history the moment somebody moved
            // on — a retroactive answer to a question this gate does not ask.
            db.MainChampionStats.Add(MainRow("retired-puuid", Yone, isMain: true, isActive: false));
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var keys = await MatchupCohort.LoadAsync(db, ["EUW1_RETIRED"], CancellationToken.None);

            keys.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task Scopes_the_result_to_the_requested_matches_only()
    {
        await _fixture.ResetDatabaseAsync();

        await using (var db = _fixture.CreateDbContext())
        {
            var account = AddAccount(db, TrackedPuuid);
            Seed(db, "EUW1_ASKED", TrackedPuuid, Yone, account.Id, participantId: 1);
            Seed(db, "EUW1_NOT_ASKED", TrackedPuuid, Yone, account.Id, participantId: 1);
            db.MainChampionStats.Add(MainRow(TrackedPuuid, Yone, isMain: true));
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var keys = await MatchupCohort.LoadAsync(db, ["EUW1_ASKED"], CancellationToken.None);

            keys.Select(key => key.MatchId).Should().BeEquivalentTo(["EUW1_ASKED"]);
        }
    }

    [Fact]
    public async Task Keys_stay_per_match_because_a_participant_id_is_only_a_slot_number()
    {
        await _fixture.ResetDatabaseAsync();

        await using (var db = _fixture.CreateDbContext())
        {
            var account = AddAccount(db, TrackedPuuid);
            // Riot's ParticipantId is the 1-10 slot inside one game, so the same number
            // names a different player in a different match. That is why the key is
            // composite and why callers must test membership with both halves.
            Seed(db, "EUW1_G1", TrackedPuuid, Yone, account.Id, participantId: 3);
            Seed(db, "EUW1_G2", TrackedPuuid, Yone, account.Id, participantId: 3);
            db.MainChampionStats.Add(MainRow(TrackedPuuid, Yone, isMain: true));
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var keys = await MatchupCohort.LoadAsync(
                db, ["EUW1_G1", "EUW1_G2"], CancellationToken.None);

            keys.Should().BeEquivalentTo(
            [
                new MatchupCohortKey("EUW1_G1", 3),
                new MatchupCohortKey("EUW1_G2", 3)
            ]);
        }
    }

    private static RiotAccount AddAccount(TrueMainDbContext db, string puuid)
    {
        var account = new RiotAccountBuilder()
            .WithPlatformId(Platform)
            .WithPuuid(puuid)
            .WithGameName("CohortMain")
            .WithTagLine("EUW")
            .Build();

        db.RiotAccounts.Add(account);
        return account;
    }

    private static void Seed(
        TrueMainDbContext db,
        string matchId,
        string puuid,
        int championId,
        Guid? riotAccountId,
        int participantId)
        => MatchParticipantSeed.AddMatchWithParticipant(
            db,
            matchId,
            Platform,
            QueueId,
            DateTime.UtcNow.AddDays(-1),
            puuid,
            championId,
            win: true,
            riotAccountId,
            participantId);

    private static MainChampionStat MainRow(
        string puuid,
        int championId,
        bool isMain,
        bool isActive = true,
        string platform = Platform)
        => new()
        {
            Id = Guid.NewGuid(),
            PlatformId = platform,
            Puuid = puuid,
            ChampionId = championId,
            TotalMatches = 100,
            ChampionMatches = 60,
            PlayRate = 0.6d,
            IsMain = isMain,
            IsActive = isActive,
            IsOtp = false,
            PrimaryPosition = "MIDDLE",
            PositionBreakdown = [new PositionStat { Position = "MIDDLE", Games = 60, Rate = 1d }],
            CalculatedAtUtc = DateTime.UtcNow,
        };
}
