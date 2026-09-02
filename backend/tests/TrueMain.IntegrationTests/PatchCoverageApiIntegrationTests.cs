using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Core.Lol.Map;
using Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Covers the patch-coverage view (#1033) — "is the current patch servable?".
///
/// <para>
/// Every assertion here needs Postgres: the coverage figure is a grouped rollup that has
/// to reproduce the champion directory's own <c>(champion, lane)</c> grouping exactly, the
/// ingestion series is raw SQL normalising <c>GameVersion</c> in the database, and the
/// one-shot folds are distinguished from empty ones by which patch they first wrote a row
/// on — none of which is checkable against an in-memory provider.
/// </para>
///
/// <para>
/// The seed builds the three states the page exists to keep apart, in one corpus:
/// <c>16.14</c> settled and fully covered, <c>16.15</c> aggregated but short of the bar
/// (the patch the site actually serves), and <c>16.16</c> ingested with no aggregate row
/// at all. Bans and per-opponent spikes exist only from <c>16.15</c> on, so the older
/// patch must read "not measured before" rather than zero.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class PatchCoverageApiIntegrationTests(PostgresFixture fixture)
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;

    /// <summary>Mirrors <c>ChampionsList:MinSampleGames</c>, the floor the public reads apply.</summary>
    private const int Floor = 10;

    private readonly PostgresFixture _fixture = fixture;

    [Fact]
    public async Task GetPatchCoverage_SeparatesAnUnaggregatedPatchFromAThinOne()
    {
        var payload = await LoadAsync();

        var byPatch = payload.Patches.ToDictionary(patch => patch.Patch, StringComparer.Ordinal);

        // The whole point of the page: both patches report few or no covered lines, and
        // they call for opposite reactions. Collapsing them into one "low coverage" number
        // is exactly the ambiguity #1033 exists to remove.
        byPatch["16.16"].Verdict.Should().Be(
            "notAggregated",
            "matches landed on 16.16 and no fold has produced a row for it yet");
        byPatch["16.16"].Matches.Should().Be(5);
        byPatch["16.16"].Lines.Should().Be(0);
        byPatch["16.16"].Headline.Should().Contain("not one aggregate row yet");

        byPatch["16.15"].Verdict.Should().Be(
            "thin",
            "16.15 is aggregated — it is short of games, not short of folds");
        byPatch["16.15"].Lines.Should().BeGreaterThan(0);

        byPatch["16.14"].Verdict.Should().Be("servable");
        byPatch["16.14"].Status.Should().Be("green");
    }

    [Fact]
    public async Task GetPatchCoverage_ServesTheNewestAggregatedPatch_NotTheNewestIngestedOne()
    {
        var payload = await LoadAsync();

        // The public reads resolve to the newest patch holding an aggregate row, so an
        // ingested-but-unfolded patch does not become "current" merely by being newer.
        // That gap is invisible on every public page, which is why the headline states it.
        payload.CurrentPatch.Should().Be("16.15");
        payload.Patches.Single(patch => patch.IsCurrent).Patch.Should().Be("16.15");
        payload.Headline.Should().Contain("16.16");
        payload.Headline.Should().Contain("still serving 16.15");
    }

    [Fact]
    public async Task GetPatchCoverage_ReadsAThinCurrentPatchAsRed()
    {
        var payload = await LoadAsync();

        var current = payload.Patches.Single(patch => patch.IsCurrent);

        // A thin patch nobody serves is history; a thin patch the site is serving right
        // now means the tier list is ranking on those lines today.
        current.Status.Should().Be("red");
        payload.Status.Should().Be("red");
        current.Headline.Should().Contain("this is the patch the site serves");
    }

    [Fact]
    public async Task GetPatchCoverage_CountsLinesAgainstTheFloorTheChampionDirectoryReadsWith()
    {
        var payload = await LoadAsync();

        payload.MinSampleGames.Should().Be(Floor);
        payload.FloorNote.Should().Contain("ChampionsList:MinSampleGames");
        payload.FloorNote.Should().Contain("10 games");

        var current = payload.Patches.Single(patch => patch.Patch == "16.15");

        // Five (champion, lane) lines were seeded on 16.15: three at 20 games, two at 4.
        // The lane-less scope row is deliberately NOT among them — the ranked directory
        // drops it, so counting it here would overstate what the site can serve.
        current.Lines.Should().Be(5, "the lane-less scope row is not a line the directory can rank");
        current.LinesPastFloor.Should().Be(3);
        current.ChampionsPastFloor.Should().Be(3);
        current.ServableLinesBar.Should().NotBeNull("a judged patch must say which bar it was judged against");
        current.ServableLinesBarNote.Should().Contain("16.15");
    }

    [Fact]
    public async Task GetPatchCoverage_NamesTheLinesStillBelowTheFloor()
    {
        var payload = await LoadAsync();

        var current = payload.Patches.Single(patch => patch.Patch == "16.15");

        current.BelowFloorCount.Should().Be(2);
        current.BelowFloor.Should().HaveCount(2);

        // Closest to the floor first, and each line says how far it still has to go — a
        // thin patch then has a named cause rather than a bare number.
        current.BelowFloor[0].Games.Should().BeGreaterThanOrEqualTo(current.BelowFloor[1].Games);
        current.BelowFloor.Select(line => line.ChampionId).Should().BeEquivalentTo([64, 81]);
        current.BelowFloor.Should().AllSatisfy(line =>
        {
            line.Position.Should().Be("MIDDLE");
            line.GamesToFloor.Should().Be(Floor - line.Games);
        });
    }

    [Fact]
    public async Task GetPatchCoverage_ReportsAOneShotFoldAsNotMeasured_NeverAsZero()
    {
        var payload = await LoadAsync();

        var older = payload.Patches.Single(patch => patch.Patch == "16.14");
        var bans = older.Folds.Single(fold => fold.Key == "bans");

        // #920 could not be backfilled: raw match payloads are not kept, so 16.14 has no
        // ban row and never will. A zero would read as "the fold is broken on this patch",
        // which is the one thing it is not.
        bans.Measured.Should().BeFalse();
        bans.Rows.Should().BeNull("a count of 0 would claim the fold ran and found nothing");
        bans.Champions.Should().BeNull();
        bans.FirstMeasuredPatch.Should().Be("16.15");
        bans.NotMeasuredNote.Should().Contain("Not measured before 16.15");

        var current = payload.Patches.Single(patch => patch.Patch == "16.15");
        current.Folds.Single(fold => fold.Key == "bans").Measured.Should().BeTrue();
        current.Folds.Single(fold => fold.Key == "bans").Rows.Should().Be(1);
    }

    [Fact]
    public async Task GetPatchCoverage_ScopesThePerOpponentSpikeFoldToTheRowsThatCarryAnOpponent()
    {
        var payload = await LoadAsync();

        // #957 splits the spike grain on the lane opponent. Rows folded before it — and
        // rows retention has collapsed — are rolled back to opponent 0, so they are not
        // per-opponent coverage even though they sit in the same table. 16.14 holds one of
        // each shape, and only the newer patch's row counts.
        var older = payload.Patches.Single(patch => patch.Patch == "16.14");
        var opponents = older.Folds.Single(fold => fold.Key == "powerspikeOpponents");

        opponents.Measured.Should().BeFalse("the only opponent-scoped row on the corpus is on 16.15");
        opponents.FirstMeasuredPatch.Should().Be("16.15");
        opponents.Rows.Should().BeNull();

        var current = payload.Patches.Single(patch => patch.Patch == "16.15");
        current.Folds.Single(fold => fold.Key == "powerspikeOpponents").Rows.Should().Be(1);
    }

    [Fact]
    public async Task GetPatchCoverage_ReportsIngestionByGameDateAndTheFoldBacklogItFeeds()
    {
        var payload = await LoadAsync();

        var older = payload.Patches.Single(patch => patch.Patch == "16.14");

        older.Matches.Should().Be(4);
        older.Participants.Should().Be(40, "each seeded match carries the full ten participants");
        older.Daily.Should().HaveCount(2, "the 16.14 matches straddle two game dates");
        older.Daily.Sum(day => day.Matches).Should().Be(older.Matches);
        older.Daily.Sum(day => day.Participants).Should().Be(older.Participants);
        older.Daily.Select(day => day.Date).Should().BeInAscendingOrder();

        // The backlog is per fold, from the flag that fold advances — it is what tells a
        // still-filling patch apart from a finished thin one.
        var bans = payload.Patches.Single(patch => patch.Patch == "16.16")
            .Folds.Single(fold => fold.Key == "bans");
        bans.PendingMatches.Should().Be(5, "none of the 16.16 matches has been ban-folded");

        // Builds are replace-by-scope per account, so "matches still to fold" is not a
        // number that exists for them — null, not zero.
        payload.Patches[0].Folds.Single(fold => fold.Key == "builds").PendingMatches.Should().BeNull();
    }

    [Fact]
    public async Task GetPatchCoverage_ReportsUnknown_OnAnEmptyCorpus()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        var payload = await client.GetFromJsonAsync<PatchCoverageContract>("/ops/patch-coverage");

        payload.Should().NotBeNull();
        payload!.Verdict.Should().Be("unknown", "an unmeasured corpus is not a passing one");
        payload.Status.Should().Be("unknown");
        payload.CurrentPatch.Should().BeNull();
        payload.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPatchCoverage_RejectsAnUnauthenticatedCaller()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/ops/patch-coverage");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- seed ----------------------------------------------------------------

    private async Task<PatchCoverageContract> LoadAsync()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateAuthedClient(factory);

        var payload = await client.GetFromJsonAsync<PatchCoverageContract>("/ops/patch-coverage");

        payload.Should().NotBeNull();
        return payload!;
    }

    private async Task SeedAsync()
    {
        var now = DateTime.UtcNow;
        await using var db = _fixture.CreateDbContext();

        var account = BuildAccount("EUW1");
        db.RiotAccounts.Add(account);

        // 16.14 — settled and fully covered. Twelve lines, every one past the floor, so it
        // is the reference the newer patches are judged against.
        for (var championId = 1; championId <= 12; championId++)
        {
            db.ChampionAggregateScopes.Add(BuildScope(account.Id, championId, "16.14", "MIDDLE", games: 20, now.AddHours(-2)));
        }

        // 16.15 — the patch the site serves. Three lines clear the floor, two do not, and
        // one scope row carries no lane at all (the non-nullable Position's "no lane"
        // sentinel), which the ranked directory drops and this page must drop with it.
        foreach (var championId in new[] { 266, 103, 84 })
        {
            db.ChampionAggregateScopes.Add(BuildScope(account.Id, championId, "16.15", "MIDDLE", games: 20, now.AddHours(-1)));
        }

        db.ChampionAggregateScopes.Add(BuildScope(account.Id, championId: 64, "16.15", "MIDDLE", games: 7, now.AddHours(-1)));
        db.ChampionAggregateScopes.Add(BuildScope(account.Id, championId: 81, "16.15", "MIDDLE", games: 4, now.AddHours(-1)));
        db.ChampionAggregateScopes.Add(BuildScope(account.Id, championId: 99, "16.15", position: string.Empty, games: 30, now.AddHours(-1)));

        // Matches: two game dates on 16.14, one on 16.15, and 16.16 ingested but unfolded.
        // Anchored to midday of a whole UTC day rather than offset from `now`, so the
        // number of distinct game dates cannot change with the hour the suite runs at —
        // `now.AddDays(-9).AddHours(1)` lands on a second date whenever it runs after 23:00.
        var midday = DateTime.UtcNow.Date.AddHours(12);

        db.Matches.Add(BuildMatch("PC_16_14_A", "16.14", midday.AddDays(-9), folded: true));
        db.Matches.Add(BuildMatch("PC_16_14_B", "16.14", midday.AddDays(-9).AddHours(1), folded: true));
        db.Matches.Add(BuildMatch("PC_16_14_C", "16.14", midday.AddDays(-8), folded: true));
        db.Matches.Add(BuildMatch("PC_16_14_D", "16.14", midday.AddDays(-8).AddHours(1), folded: true));

        db.Matches.Add(BuildMatch("PC_16_15_A", "16.15", midday.AddDays(-3), folded: true));
        db.Matches.Add(BuildMatch("PC_16_15_B", "16.15", midday.AddDays(-3).AddHours(2), folded: true));

        for (var index = 0; index < 5; index++)
        {
            db.Matches.Add(BuildMatch($"PC_16_16_{index}", "16.16", midday.AddDays(-1).AddHours(index), folded: false));
        }

        foreach (var matchId in new[]
                 {
                     "PC_16_14_A", "PC_16_14_B", "PC_16_14_C", "PC_16_14_D",
                     "PC_16_15_A", "PC_16_15_B",
                     "PC_16_16_0", "PC_16_16_1", "PC_16_16_2", "PC_16_16_3", "PC_16_16_4"
                 })
        {
            for (var participantId = 1; participantId <= 10; participantId++)
            {
                db.MatchParticipants.Add(BuildParticipant(matchId, participantId));
            }
        }

        // Bans exist only from 16.15 on — the #920 shape: the fold shipped mid-corpus and
        // the matches before it were flagged as already folded, so 16.14 can never gain a
        // ban row.
        db.ChampionBanStats.Add(new ChampionBanStat
        {
            Id = Guid.NewGuid(),
            ChampionId = 266,
            Patch = "16.15",
            EloBracket = "ALL",
            Bans = 3,
            AggregatedAtUtc = now.AddHours(-1)
        });
        db.BanScopeTotals.Add(new BanScopeTotal
        {
            Id = Guid.NewGuid(),
            Patch = "16.15",
            EloBracket = "ALL",
            Matches = 10,
            AggregatedAtUtc = now.AddHours(-1)
        });

        // Spike rows on both patches, but only the newer one carries a lane opponent
        // (#957). The 16.14 row is the pre-#957 shape rolled back to opponent 0.
        db.ChampionPowerspikeEventStats.Add(BuildSpikeEvent("16.14", opponentChampionId: 0, now.AddHours(-2)));
        db.ChampionPowerspikeEventStats.Add(BuildSpikeEvent("16.15", opponentChampionId: 103, now.AddHours(-1)));

        await db.SaveChangesAsync();
    }

    // ---- builders ------------------------------------------------------------

    private static RiotAccount BuildAccount(string platformId) => new()
    {
        Id = Guid.NewGuid(),
        Puuid = $"{platformId}-{Guid.NewGuid():N}",
        GameName = $"{platformId}Main",
        TagLine = platformId,
        PlatformId = platformId,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static ChampionAggregateScope BuildScope(
        Guid accountId,
        int championId,
        string gameVersion,
        string position,
        int games,
        DateTime aggregatedAt) => new()
    {
        Id = Guid.NewGuid(),
        RiotAccountId = accountId,
        ChampionId = championId,
        GameVersion = gameVersion,
        PlatformId = "EUW1",
        QueueId = (int)LolQueueId.RankedSoloDuo,
        Position = position,
        EloBracket = "GOLD",
        // Mains: the population these fixtures have always described; a
        // non-nullable bool is always written, so the column default never
        // applies and an unset flag would seed a non-main (#1346).
        IsMain = true,
        Games = games,
        Wins = games / 2,
        AggregatedAtUtc = aggregatedAt
    };

    /// <summary>
    /// <paramref name="folded"/> drives every per-match aggregation flag at once: the
    /// backlog assertions only care that an unfolded patch reports its matches as pending
    /// on every fold, not which fold lags which.
    /// </summary>
    private static Match BuildMatch(string id, string patch, DateTime gameStart, bool folded) => new()
    {
        Id = id,
        PlatformId = "EUW1",
        QueueId = (int)LolQueueId.RankedSoloDuo,
        MapId = (int)LolMapId.SummonersRift,
        GameMode = "CLASSIC",
        GameType = "MATCHED_GAME",
        GameStartTimeUtc = gameStart,
        GameDurationSeconds = 1800,
        // The raw four-segment form Riot sends: the rollup has to normalise it in SQL, so
        // seeding the already-normalised form would test nothing.
        GameVersion = $"{patch}.1.1",
        CreatedAtUtc = DateTime.UtcNow,
        TimelineIngested = true,
        PowerspikeAggregated = folded,
        SynergyAggregated = folded,
        MatchupLeadAggregated = folded,
        BansAggregated = folded,
        LaneOutcomeAggregated = folded
    };

    private static MatchParticipant BuildParticipant(string matchId, int participantId) => new()
    {
        Id = Guid.NewGuid(),
        MatchId = matchId,
        ParticipantId = participantId,
        Puuid = $"{matchId}-p{participantId}",
        SummonerName = $"{matchId}-p{participantId}",
        SummonerLevel = 100,
        ChampionId = 100 + participantId,
        TeamId = participantId <= 5 ? 100 : 200,
        TeamPosition = "MIDDLE",
        IndividualPosition = "MIDDLE",
        Lane = "MIDDLE",
        Role = "NONE",
        Win = participantId <= 5,
        ItemEvents = [],
        SkillEvents = []
    };

    private static ChampionPowerspikeEventStat BuildSpikeEvent(
        string patch,
        int opponentChampionId,
        DateTime aggregatedAt) => new()
    {
        Id = Guid.NewGuid(),
        ChampionId = 266,
        TeamPosition = "MIDDLE",
        Patch = patch,
        EloBracket = "GOLD",
        BuildFirstItemId = 6630,
        BuildKeystoneId = 8010,
        OpponentChampionId = opponentChampionId,
        EventType = "level",
        RefId = 6,
        SumSpike = 1.5,
        SumMinute = 8.5,
        Games = 12,
        AggregatedAtUtc = aggregatedAt
    };

    private static HttpClient CreateAuthedClient(ApiWebApplicationFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Ops-Key", OpsApiKey);
        return client;
    }

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(fixture);

    // ---- wire contracts ------------------------------------------------------

    private sealed class PatchCoverageContract
    {
        public int MinSampleGames { get; init; }
        public string FloorNote { get; init; } = string.Empty;
        public string? CurrentPatch { get; init; }
        public string Verdict { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string Headline { get; init; } = string.Empty;
        public IReadOnlyList<PatchRowContract> Patches { get; init; } = [];
    }

    private sealed class PatchRowContract
    {
        public string Patch { get; init; } = string.Empty;
        public bool IsCurrent { get; init; }
        public string Verdict { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string Headline { get; init; } = string.Empty;
        public long Matches { get; init; }
        public long Participants { get; init; }
        public IReadOnlyList<DayContract> Daily { get; init; } = [];
        public long Lines { get; init; }
        public long LinesPastFloor { get; init; }
        public long Champions { get; init; }
        public long ChampionsPastFloor { get; init; }
        public double? ServableLinesBar { get; init; }
        public string? ServableLinesBarNote { get; init; }
        public long BelowFloorCount { get; init; }
        public IReadOnlyList<ThinLineContract> BelowFloor { get; init; } = [];
        public IReadOnlyList<FoldContract> Folds { get; init; } = [];
    }

    private sealed class DayContract
    {
        public string Date { get; init; } = string.Empty;
        public long Matches { get; init; }
        public long Participants { get; init; }
    }

    private sealed class ThinLineContract
    {
        public int ChampionId { get; init; }
        public string Position { get; init; } = string.Empty;
        public long Games { get; init; }
        public long GamesToFloor { get; init; }
    }

    private sealed class FoldContract
    {
        public string Key { get; init; } = string.Empty;
        public bool Measured { get; init; }
        public string? FirstMeasuredPatch { get; init; }
        public string? NotMeasuredNote { get; init; }
        public long? Rows { get; init; }
        public long? Champions { get; init; }
        public string Status { get; init; } = string.Empty;
        public long? PendingMatches { get; init; }
    }
}
