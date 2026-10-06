using AwesomeAssertions;
using Data;
using Data.Aggregation;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Pins <see cref="ChampionCohort"/> directly, one clause of its predicate at a time.
/// Four folds read this set and the champion aggregates the matchups panel is read
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
public sealed class ChampionCohortIntegrationTests : IAsyncLifetime
{
    private const int QueueId = 420;
    private const int Yone = 157;
    private const int Zed = 238;
    private const string Platform = "EUW1";
    private const string TrackedPuuid = "cohort-main-puuid";

    private readonly PostgresFixture _fixture;

    public ChampionCohortIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task ReturnsAnEmptySetWhenNoMatchesAreAskedFor()
    {
        await using var db = _fixture.CreateDbContext();

        var cohort = await ChampionCohort.LoadAsync(db, [], CancellationToken.None);

        cohort.Keys.Should().BeEmpty();
    }

    [Fact]
    public async Task AdmitsATrackedParticipantThatMainsTheChampionItPlayed()
    {
        await using (var db = _fixture.CreateDbContext())
        {
            var account = AddAccount(db, TrackedPuuid);
            Seed(db, "EUW1_MAIN", TrackedPuuid, Yone, account.Id, participantId: 4);
            db.MainChampionStats.Add(MainRow(TrackedPuuid, Yone, isMain: true));
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var cohort = await ChampionCohort.LoadAsync(db, ["EUW1_MAIN"], CancellationToken.None);

            cohort.Keys.Should().ContainSingle()
                .Which.Should().Be(new ChampionCohortKey("EUW1_MAIN", 4));
        }
    }

    [Fact]
    public async Task ExcludesAnUntrackedParticipantEvenWhenAMainRowExistsForItsPuuid()
    {
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
            var cohort = await ChampionCohort.LoadAsync(db, ["EUW1_ORPHAN"], CancellationToken.None);

            cohort.Keys.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ExcludesATrackedParticipantPlayingAChampionItDoesNotMain()
    {
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
            var cohort = await ChampionCohort.LoadAsync(
                db, ["EUW1_OFFMAIN", "EUW1_ONMAIN"], CancellationToken.None);

            cohort.Keys.Select(key => key.MatchId).Should().BeEquivalentTo(["EUW1_ONMAIN"]);
        }
    }

    [Fact]
    public async Task ExcludesAParticipantWhoseMainRowWasComputedOnAnotherPlatform()
    {
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
            var cohort = await ChampionCohort.LoadAsync(db, ["EUW1_XPLAT"], CancellationToken.None);

            cohort.Keys.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task KeepsARetiredMainBecauseIsActiveOnlyRetiresFutureIngestion()
    {
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
            var cohort = await ChampionCohort.LoadAsync(db, ["EUW1_RETIRED"], CancellationToken.None);

            cohort.Keys.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task ScopesTheResultToTheRequestedMatchesOnly()
    {
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
            var cohort = await ChampionCohort.LoadAsync(db, ["EUW1_ASKED"], CancellationToken.None);

            cohort.Keys.Select(key => key.MatchId).Should().BeEquivalentTo(["EUW1_ASKED"]);
        }
    }

    [Fact]
    public async Task Keys_StayPerMatchBecauseAParticipantIdIsOnlyASlotNumber()
    {
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
            var cohort = await ChampionCohort.LoadAsync(
                db, ["EUW1_G1", "EUW1_G2"], CancellationToken.None);

            cohort.Keys.Should().BeEquivalentTo(
            [
                new ChampionCohortKey("EUW1_G1", 3),
                new ChampionCohortKey("EUW1_G2", 3)
            ]);
        }
    }

    [Fact]
    public async Task ExcludesARemadeGameBecauseARemakeIsNotAGame()
    {
        await using (var db = _fixture.CreateDbContext())
        {
            var account = AddAccount(db, TrackedPuuid);
            // Under the 5-minute floor: the pre-vote remake every fold used to decide
            // for itself whether to count; the duration floor is the signal for every
            // match Riot's flag does not cover (#1365, #1364).
            Seed(db, "EUW1_REMAKE", TrackedPuuid, Yone, account.Id, participantId: 1,
                gameDurationSeconds: ChampionCohort.MinimumGameDurationSeconds - 1);
            Seed(db, "EUW1_GAME", TrackedPuuid, Yone, account.Id, participantId: 1,
                gameDurationSeconds: ChampionCohort.MinimumGameDurationSeconds);
            db.MainChampionStats.Add(MainRow(TrackedPuuid, Yone, isMain: true));
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var cohort = await ChampionCohort.LoadAsync(
                db, ["EUW1_REMAKE", "EUW1_GAME"], CancellationToken.None);

            cohort.Keys.Select(key => key.MatchId).Should().BeEquivalentTo(["EUW1_GAME"]);
            cohort.IncludesMatch("EUW1_GAME").Should().BeTrue();
            cohort.IncludesMatch("EUW1_REMAKE").Should()
                .BeFalse("a remake contributes nothing at all, not even to a population-wide normaliser");
        }
    }

    [Fact]
    public async Task ExcludesAGameRiotFlaggedAsARemakeWhateverItsDuration()
    {
        await using (var db = _fixture.CreateDbContext())
        {
            var account = AddAccount(db, TrackedPuuid);
            // Over the floor, but Riot's own remake vote says it was not a game (#1364).
            Seed(db, "EUW1_FLAGGED", TrackedPuuid, Yone, account.Id, participantId: 1,
                gameDurationSeconds: ChampionCohort.MinimumGameDurationSeconds + 60);
            Seed(db, "EUW1_GAME", TrackedPuuid, Yone, account.Id, participantId: 1);
            db.Matches.Local.Single(match => match.Id == "EUW1_FLAGGED").EndedInEarlySurrender = true;
            db.MainChampionStats.Add(MainRow(TrackedPuuid, Yone, isMain: true));
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var cohort = await ChampionCohort.LoadAsync(
                db, ["EUW1_FLAGGED", "EUW1_GAME"], CancellationToken.None);

            cohort.Keys.Select(key => key.MatchId).Should().BeEquivalentTo(["EUW1_GAME"]);
            cohort.IncludesMatch("EUW1_FLAGGED").Should().BeFalse();
        }
    }

    [Fact]
    public async Task ExcludesAParticipantWhosePositionIsNotCanonical()
    {
        await using (var db = _fixture.CreateDbContext())
        {
            var account = AddAccount(db, TrackedPuuid);
            // An empty TeamPosition cannot be placed in a lane, a matchup or a
            // composition, so it is nobody's champion side.
            Seed(db, "EUW1_NOLANE", TrackedPuuid, Yone, account.Id, participantId: 1, teamPosition: "");
            db.MainChampionStats.Add(MainRow(TrackedPuuid, Yone, isMain: true));
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var cohort = await ChampionCohort.LoadAsync(db, ["EUW1_NOLANE"], CancellationToken.None);

            cohort.Keys.Should().BeEmpty();
            cohort.IncludesMatch("EUW1_NOLANE").Should()
                .BeTrue("the match is still a game - it just has nobody on the champion side");
        }
    }

    /// <summary>
    /// The folds load the cohort per batch and the live reads compose it as a query; both
    /// are the one predicate, and this pins that they answer alike on a corpus holding
    /// every case the clauses above separate (#1365).
    /// </summary>
    [Fact]
    public async Task TheLiveReadQueryAndTheFoldLoadAdmitTheSameParticipants()
    {
        string[] matchIds =
            ["EUW1_A_MAIN", "EUW1_A_OFFMAIN", "EUW1_A_ORPHAN", "EUW1_A_REMAKE", "EUW1_A_NOLANE", "EUW1_A_RETIRED"];

        await using (var db = _fixture.CreateDbContext())
        {
            var main = AddAccount(db, TrackedPuuid);
            var retired = AddAccount(db, "retired-puuid");
            Seed(db, "EUW1_A_MAIN", TrackedPuuid, Yone, main.Id, participantId: 1);
            Seed(db, "EUW1_A_OFFMAIN", TrackedPuuid, Zed, main.Id, participantId: 2);
            Seed(db, "EUW1_A_ORPHAN", "orphan-puuid", Yone, riotAccountId: null, participantId: 3);
            Seed(db, "EUW1_A_REMAKE", TrackedPuuid, Yone, main.Id, participantId: 4,
                gameDurationSeconds: ChampionCohort.MinimumGameDurationSeconds - 1);
            Seed(db, "EUW1_A_NOLANE", TrackedPuuid, Yone, main.Id, participantId: 5, teamPosition: "");
            Seed(db, "EUW1_A_RETIRED", "retired-puuid", Yone, retired.Id, participantId: 6);
            db.MainChampionStats.AddRange(
                MainRow(TrackedPuuid, Yone, isMain: true),
                MainRow(TrackedPuuid, Zed, isMain: false),
                MainRow("orphan-puuid", Yone, isMain: true),
                MainRow("retired-puuid", Yone, isMain: true, isActive: false));
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var loaded = await ChampionCohort.LoadAsync(db, matchIds, CancellationToken.None);
            var composed = await ChampionCohort.Members(db, QueueId, patch: null)
                .Select(p => new ChampionCohortKey(p.MatchId, p.ParticipantId))
                .ToListAsync();

            composed.Should().BeEquivalentTo(loaded.Keys);
            composed.Select(key => key.MatchId).Should().BeEquivalentTo(["EUW1_A_MAIN", "EUW1_A_RETIRED"]);
        }
    }

    [Fact]
    public async Task Games_DropsTheRemakesAndKeepsTheQueueAndPatchAskedFor()
    {
        await using (var db = _fixture.CreateDbContext())
        {
            var account = AddAccount(db, TrackedPuuid);
            Seed(db, "EUW1_G_GAME", TrackedPuuid, Yone, account.Id, participantId: 1);
            Seed(db, "EUW1_G_REMAKE", TrackedPuuid, Yone, account.Id, participantId: 1,
                gameDurationSeconds: ChampionCohort.MinimumGameDurationSeconds - 1);
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var games = await ChampionCohort.Games(db, QueueId, patch: null).Select(m => m.Id).ToListAsync();
            var otherQueue = await ChampionCohort.Games(db, 440, patch: null).Select(m => m.Id).ToListAsync();

            games.Should().BeEquivalentTo(["EUW1_G_GAME"]);
            otherQueue.Should().BeEmpty();
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
        int participantId,
        int gameDurationSeconds = 1800,
        string teamPosition = "BOTTOM")
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
            participantId,
            gameDurationSeconds,
            teamPosition);

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
