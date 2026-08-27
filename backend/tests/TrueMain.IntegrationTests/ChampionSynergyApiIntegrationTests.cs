using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Core.Lol.Map;
using Core.Options;
using Data.Entities;
using Ingestor.Options;
using Ingestor.Processes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.ReadModels.Champions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// End-to-end cover for the synergies endpoints (#922). The duo cases seed the
/// aggregate directly so the scoring model can be checked against numbers chosen
/// to make the point: the partner with the <em>lower</em> raw pair win rate must
/// still rank first when it is the one beating its expectation. The trio case goes
/// through the real fold, since its answer is a live join over participant rows.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChampionSynergyApiIntegrationTests
{
    private const int QueueId = 420;
    private const int Champion = 157; // Yone, MIDDLE — the tracked side.
    private const string Position = "MIDDLE";
    private const int Support = 350; // Yuumi, UTILITY
    private const int Adc = 81; // Ezreal, BOTTOM
    private const int Jungler = 64; // Lee Sin, JUNGLE
    private const int Top = 86; // Garen, TOP
    private const int ThinSampleChampion = 99; // Lux, MIDDLE — seeded below every floor.
    private const string Patch = "16.4";

    private readonly PostgresFixture _fixture;

    public ChampionSynergyApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetChampionSynergiesAsync_RanksByExcessOverExpected_NotByRawPairWinRate()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAggregateAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/champions/{Champion}/synergies?position={Position}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var synergies = await response.Content.ReadFromJsonAsync<ChampionSynergiesResponse>();
        synergies.Should().NotBeNull();
        synergies!.ChampionId.Should().Be(Champion);
        synergies.Position.Should().Be(Position);
        synergies.Patch.Should().BeNull("no patch was pinned, so the slice spans every patch");
        synergies.ChampionGames.Should().Be(100);
        synergies.ChampionWinRate.Should().BeApproximately(0.5, 1e-9);
        synergies.CohortWinRate.Should().BeApproximately(0.5, 1e-9, "both SELF rows sit at 50%");

        // Yuumi: 13/20 = 65% together, but a 50% champion beside a 50% champion in a
        // 50% cohort is expected to win 50% — so +15 points of genuine synergy.
        // Ezreal: 14/20 = 70% together, a higher raw number, but Ezreal's teams win
        // 70% of the time regardless, so the pairing adds nothing.
        var yuumi = synergies.Partners.Should().ContainSingle(p => p.PartnerChampionId == Support).Subject;
        yuumi.PartnerPosition.Should().Be("UTILITY");
        yuumi.Games.Should().Be(20);
        yuumi.WinRate.Should().BeApproximately(0.65, 1e-9);
        yuumi.ExpectedWinRate.Should().BeApproximately(0.5, 1e-9);
        yuumi.Synergy.Should().BeApproximately(0.15, 1e-9);

        var ezreal = synergies.Partners.Should().ContainSingle(p => p.PartnerChampionId == Adc).Subject;
        ezreal.WinRate.Should().BeApproximately(0.7, 1e-9)
            .And.BeGreaterThan(yuumi.WinRate, "Ezreal's raw pair win rate is the higher of the two");
        ezreal.ExpectedWinRate.Should().BeApproximately(0.7, 1e-9);
        ezreal.Synergy.Should().BeApproximately(0, 1e-9);

        synergies.Partners[0].PartnerChampionId.Should().Be(
            Support,
            "the list is ranked by synergy, so the weaker raw win rate leads");
    }

    [Fact]
    public async Task GetChampionSynergiesAsync_DropsPairsAndBaselinesBelowTheirFloors()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAggregateAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var synergies = await client.GetFromJsonAsync<ChampionSynergiesResponse>(
            $"/champions/{Champion}/synergies?position={Position}");

        synergies!.MinGames.Should().Be(10);
        synergies.Partners.Should().NotContain(
            p => p.PartnerChampionId == Jungler,
            "the Lee Sin pairing has 5 games, below the pair floor");
        synergies.Partners.Should().NotContain(
            p => p.PartnerChampionId == Top,
            "the Garen pairing clears the pair floor but its baseline is 15 games, below the baseline floor");

        synergies.Partners.Should().AllSatisfy(partner =>
        {
            partner.Games.Should().BeGreaterThanOrEqualTo(synergies.MinGames);
            partner.PartnerBaselineGames.Should().BeGreaterThanOrEqualTo(partner.Games);
        });
    }

    [Fact]
    public async Task GetChampionSynergiesAsync_DropsAPartnerOnALaneItBarelyPlays()
    {
        await _fixture.ResetDatabaseAsync();
        // Garen at BOTTOM: the pairing clears the games floor and the baseline floor,
        // and BOTTOM holds 25 of his 325 ally games — 7.7%, under the 10% floor. This
        // is the "Sylas BOTTOM" line that topped Viego JUNGLE's synergies on
        // production: a real count of games behind a role the champion does not play,
        // which no reader can act on however good the win rate is.
        await SeedOffRolePartnerAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var synergies = await client.GetFromJsonAsync<ChampionSynergiesResponse>(
            $"/champions/{Champion}/synergies?position={Position}");

        synergies!.Partners.Should().NotContain(
            p => p.PartnerChampionId == Top && p.PartnerPosition == "BOTTOM",
            "BOTTOM is 7.7% of this champion's games — a role-detection artefact, not a duo");
        synergies.Partners.Should().Contain(
            p => p.PartnerChampionId == Top && p.PartnerPosition == "TOP",
            "the same partner on the lane it actually plays is a real pairing");
    }

    [Fact]
    public async Task GetChampionSynergiesAsync_ScalesThePairFloorWithTheChampionsVolume()
    {
        await _fixture.ResetDatabaseAsync();
        // 3 000 champion games, so the share floor (1%) is 30 and outranks the
        // absolute floor of 10. A 25-game pairing is comfortably above the absolute
        // floor and still 0.8% of the champion's games — the shape that filled the
        // top of the ranking with pairings nobody has played.
        await SeedHighVolumeChampionAsync(championGames: 3_000, thinPairGames: 25, thickPairGames: 60);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var synergies = await client.GetFromJsonAsync<ChampionSynergiesResponse>(
            $"/champions/{Champion}/synergies?position={Position}");

        synergies!.MinGames.Should().Be(30, "the share floor binds and is echoed back");
        synergies.Partners.Should().NotContain(p => p.PartnerChampionId == Support);

        var adc = synergies.Partners.Should().ContainSingle(p => p.PartnerChampionId == Adc).Subject;
        adc.Games.Should().Be(60);
        adc.PlayRate.Should().BeApproximately(60d / 3_000d, 1e-9);
    }

    [Fact]
    public async Task GetChampionSynergiesAsync_NarrowsToOnePartnerLane_WithoutMovingTheNumbers()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAggregateAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var all = await client.GetFromJsonAsync<ChampionSynergiesResponse>(
            $"/champions/{Champion}/synergies?position={Position}");
        var utility = await client.GetFromJsonAsync<ChampionSynergiesResponse>(
            $"/champions/{Champion}/synergies?position={Position}&partnerPosition=UTILITY");

        utility!.PartnerPosition.Should().Be("UTILITY");
        utility.Partners.Should().OnlyContain(p => p.PartnerPosition == "UTILITY");

        // The cohort reference point is a property of the scope, not of the list, so
        // narrowing the partner lane must not shift the synergy already reported.
        utility.CohortWinRate.Should().Be(all!.CohortWinRate);
        utility.Partners.Single(p => p.PartnerChampionId == Support).Synergy
            .Should().BeApproximately(all.Partners.Single(p => p.PartnerChampionId == Support).Synergy, 1e-12);
    }

    [Fact]
    public async Task GetChampionSynergiesAsync_ReturnsNoEntriesRatherThanInventedOnes_WhenTheChampionSampleIsThin()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAggregateAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // Lux has a SELF baseline of 12 games — below the 20-game baseline floor —
        // so no expected win rate can be built for her, and the response says so
        // with a real game count instead of returning a made-up ranking.
        var synergies = await client.GetFromJsonAsync<ChampionSynergiesResponse>(
            $"/champions/{ThinSampleChampion}/synergies?position={Position}");

        synergies!.ChampionGames.Should().Be(12);
        synergies.Partners.Should().BeEmpty();
    }

    [Fact]
    public async Task GetChampionSynergiesAsync_RejectsAnUnknownPartnerPosition()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync(
            $"/champions/{Champion}/synergies?position={Position}&partnerPosition=BANANA");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetChampionTrioSynergiesAsync_ExtendsTheDuoWithItsRemainingTeammates()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedFoldedGamesAsync(games: 24, wins: 12);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var trios = await client.GetFromJsonAsync<ChampionTrioSynergiesResponse>(
            $"/champions/{Champion}/synergies/trios?position={Position}&partner={Support}&partnerPosition=UTILITY");

        trios.Should().NotBeNull();
        trios!.PairGames.Should().Be(24);
        trios.PairWins.Should().Be(12);
        trios.MinGames.Should().Be(5);

        // The remaining three lanes of the tracked player's own team — never the
        // duo's own two lanes, and never anyone from the enemy team.
        trios.Completions.Select(c => (c.ChampionId, c.Position))
            .Should().BeEquivalentTo(new[] { (Top, "TOP"), (Jungler, "JUNGLE"), (Adc, "BOTTOM") });
        trios.Completions.Should().AllSatisfy(completion =>
        {
            completion.Games.Should().Be(24);
            completion.BaselineGames.Should().BeGreaterThanOrEqualTo(completion.Games);
        });
    }

    [Fact]
    public async Task GetChampionTrioSynergiesAsync_ReportsTheDuoSampleWhenItIsTooSmallToSplit()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedFoldedGamesAsync(games: 3, wins: 2);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var trios = await client.GetFromJsonAsync<ChampionTrioSynergiesResponse>(
            $"/champions/{Champion}/synergies/trios?position={Position}&partner={Support}&partnerPosition=UTILITY");

        // Three shared games can never support a third dimension. The caller still
        // gets the real count so it can say why rather than imply "no third works".
        trios!.PairGames.Should().Be(3);
        trios.Completions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetChampionTrioSynergiesAsync_RejectsAPartnerInTheSameLane()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync(
            $"/champions/{Champion}/synergies/trios?position={Position}&partner={Support}&partnerPosition={Position}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetChampionSynergiesAsync_OrdersTiedSynergiesByPartnerAndLane()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedTiedSynergyPartnersAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var first = await client.GetFromJsonAsync<ChampionSynergiesResponse>(
            $"/champions/{Champion}/synergies?position={Position}");
        var second = await client.GetFromJsonAsync<ChampionSynergiesResponse>(
            $"/champions/{Champion}/synergies?position={Position}");

        // Two pairings with the same baselines and the same record score identically —
        // synergy is a difference of two rates, so ties are the norm rather than the
        // corner case. (champion, lane) breaks them, so Lee Sin (64) precedes Garen
        // (86) and two identical requests can never reshuffle the panel.
        first!.Partners.Should().HaveCount(2);
        first.Partners[1].Synergy.Should().BeApproximately(first.Partners[0].Synergy, 1e-9);
        first.Partners.Select(p => p.PartnerChampionId).Should().Equal(Jungler, Top);
        second!.Partners.Select(p => p.PartnerChampionId)
            .Should().Equal(first.Partners.Select(p => p.PartnerChampionId));
    }

    /// <summary>
    /// Seeds two partners whose baselines and pair records are identical, so their
    /// synergy is the same number and only the tie-break decides the order.
    /// </summary>
    private async Task SeedTiedSynergyPartnersAsync()
    {
        await using var db = _fixture.CreateDbContext();

        db.ChampionSynergyBaselineStats.AddRange(
            Baseline(Champion, Position, SynergyBaselineSide.Self, games: 100, wins: 50),
            Baseline(103, Position, SynergyBaselineSide.Self, games: 100, wins: 50),
            Baseline(Jungler, "JUNGLE", SynergyBaselineSide.Ally, games: 100, wins: 50),
            Baseline(Top, "TOP", SynergyBaselineSide.Ally, games: 100, wins: 50));

        db.ChampionSynergyStats.AddRange(
            Pair(Jungler, "JUNGLE", games: 20, wins: 13),
            Pair(Top, "TOP", games: 20, wins: 13));

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds the pair and baseline aggregates directly, so the scoring model is
    /// tested against chosen numbers rather than against whatever a fold happens
    /// to produce.
    /// </summary>
    private async Task SeedAggregateAsync()
    {
        await using var db = _fixture.CreateDbContext();

        db.ChampionSynergyBaselineStats.AddRange(
            Baseline(Champion, Position, SynergyBaselineSide.Self, games: 100, wins: 50),
            // A second SELF row so the cohort is not simply the queried champion's
            // own baseline. Every SELF row sits at exactly 50%, including the thin
            // one below, so the intercept stays 50% and the expected values in the
            // ranking test are readable by hand.
            Baseline(103, Position, SynergyBaselineSide.Self, games: 100, wins: 50),
            Baseline(ThinSampleChampion, Position, SynergyBaselineSide.Self, games: 12, wins: 6),
            Baseline(Support, "UTILITY", SynergyBaselineSide.Ally, games: 100, wins: 50),
            Baseline(Adc, "BOTTOM", SynergyBaselineSide.Ally, games: 100, wins: 70),
            Baseline(Jungler, "JUNGLE", SynergyBaselineSide.Ally, games: 100, wins: 50),
            Baseline(Top, "TOP", SynergyBaselineSide.Ally, games: 15, wins: 8));

        db.ChampionSynergyStats.AddRange(
            Pair(Support, "UTILITY", games: 20, wins: 13),
            Pair(Adc, "BOTTOM", games: 20, wins: 14),
            Pair(Jungler, "JUNGLE", games: 5, wins: 4),
            Pair(Top, "TOP", games: 12, wins: 9));

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds one partner seen on two lanes — its real one and one it barely plays —
    /// with both pairings well clear of every games floor, so only the lane-share
    /// filter can tell them apart.
    /// </summary>
    private async Task SeedOffRolePartnerAsync()
    {
        await using var db = _fixture.CreateDbContext();

        db.ChampionSynergyBaselineStats.AddRange(
            Baseline(Champion, Position, SynergyBaselineSide.Self, games: 100, wins: 50),
            Baseline(103, Position, SynergyBaselineSide.Self, games: 100, wins: 50),
            Baseline(Top, "TOP", SynergyBaselineSide.Ally, games: 300, wins: 150),
            Baseline(Top, "BOTTOM", SynergyBaselineSide.Ally, games: 25, wins: 18));

        db.ChampionSynergyStats.AddRange(
            Pair(Top, "TOP", games: 20, wins: 11),
            Pair(Top, "BOTTOM", games: 20, wins: 16));

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds a champion played enough that the share floor overtakes the absolute
    /// one, with a pairing on each side of it.
    /// </summary>
    private async Task SeedHighVolumeChampionAsync(int championGames, int thinPairGames, int thickPairGames)
    {
        await using var db = _fixture.CreateDbContext();

        db.ChampionSynergyBaselineStats.AddRange(
            Baseline(Champion, Position, SynergyBaselineSide.Self, championGames, wins: championGames / 2),
            Baseline(103, Position, SynergyBaselineSide.Self, games: 100, wins: 50),
            Baseline(Support, "UTILITY", SynergyBaselineSide.Ally, games: 400, wins: 200),
            Baseline(Adc, "BOTTOM", SynergyBaselineSide.Ally, games: 400, wins: 200));

        db.ChampionSynergyStats.AddRange(
            Pair(Support, "UTILITY", thinPairGames, wins: (int)(thinPairGames * 0.8)),
            Pair(Adc, "BOTTOM", thickPairGames, wins: (int)(thickPairGames * 0.6)));

        await db.SaveChangesAsync();
    }

    private static ChampionSynergyStat Pair(int partnerChampionId, string partnerPosition, int games, int wins)
        => new()
        {
            ChampionId = Champion,
            TeamPosition = Position,
            PartnerChampionId = partnerChampionId,
            PartnerPosition = partnerPosition,
            Patch = Patch,
            EloBracket = "GOLD",
            Games = games,
            Wins = wins,
            AggregatedAtUtc = DateTime.UtcNow
        };

    private static ChampionSynergyBaselineStat Baseline(
        int championId, string position, string side, int games, int wins)
        => new()
        {
            ChampionId = championId,
            TeamPosition = position,
            Side = side,
            Patch = Patch,
            EloBracket = "GOLD",
            Games = games,
            Wins = wins,
            AggregatedAtUtc = DateTime.UtcNow
        };

    /// <summary>
    /// Seeds raw ten-player games and runs the real fold over them, so the trio
    /// cases read baselines produced the same way production produces them while
    /// the participant rows their live join needs are still on disk.
    /// </summary>
    private async Task SeedFoldedGamesAsync(int games, int wins)
    {
        (int ChampionId, string Position)[] allies =
        [
            (Top, "TOP"),
            (Jungler, "JUNGLE"),
            (Adc, "BOTTOM"),
            (Support, "UTILITY")
        ];

        (int ChampionId, string Position)[] enemies =
        [
            (122, "TOP"),
            (60, "JUNGLE"),
            (238, "MIDDLE"),
            (222, "BOTTOM"),
            (412, "UTILITY")
        ];

        await using (var db = _fixture.CreateDbContext())
        {
            var account = new RiotAccountBuilder()
                .WithGameName("SynergyMain")
                .WithTagLine("KR1")
                .WithPuuid("synergy-api-puuid")
                .Build();
            db.RiotAccounts.Add(account);

            for (var i = 0; i < games; i++)
            {
                var matchId = $"syn-{i}";
                db.Matches.Add(new MatchBuilder()
                    .WithId(matchId)
                    .WithQueueId(QueueId)
                    .WithGameVersion($"{Patch}.521.123")
                    .WithTimelineIngested()
                    .Build());

                var win = i < wins;
                var participantId = 1;

                db.MatchParticipants.Add(Participant(
                    matchId, participantId++, Champion, Position, teamId: 100, win: win, riotAccountId: account.Id));
                foreach (var (allyChampion, allyPosition) in allies)
                {
                    db.MatchParticipants.Add(Participant(matchId, participantId++, allyChampion, allyPosition, 100, win));
                }
                foreach (var (enemyChampion, enemyPosition) in enemies)
                {
                    db.MatchParticipants.Add(Participant(matchId, participantId++, enemyChampion, enemyPosition, 200, !win));
                }
            }

            await db.SaveChangesAsync();
        }

        var process = new ChampionSynergyAggregationProcess(
            NullLogger<ChampionSynergyAggregationProcess>.Instance,
            Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions { QueueId = LolQueueId.RankedSoloDuo }),
            Microsoft.Extensions.Options.Options.Create(new SynergyAggregationOptions()),
            new TestDbContextFactory(_fixture),
            TimeProvider.System);

        await process.RunCoreAsync(CancellationToken.None);
    }

    private static MatchParticipant Participant(
        string matchId, int participantId, int championId, string position, int teamId, bool win,
        Guid? riotAccountId = null)
        => new()
        {
            MatchId = matchId,
            ParticipantId = participantId,
            Puuid = $"puuid-{matchId}-{participantId}",
            RiotAccountId = riotAccountId,
            SummonerName = "seed",
            SummonerLevel = 100,
            ChampionId = championId,
            TeamId = teamId,
            TeamPosition = position,
            IndividualPosition = position,
            Lane = position,
            Role = "SOLO",
            Win = win,
            ChampLevel = 16,
            Item6 = 3363,
            TrinketItemId = 3363,
            ItemEvents = [],
            SkillEvents = []
        };

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [
                new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420"),
                new KeyValuePair<string, string?>("ChampionsList:MinSynergyGames", "10"),
                new KeyValuePair<string, string?>("ChampionsList:MinSynergyPlayRate", "0.01"),
                new KeyValuePair<string, string?>("ChampionsList:MinSynergyPartnerLanePlayRate", "0.10"),
                new KeyValuePair<string, string?>("ChampionsList:MinSynergyTrioGames", "5"),
                new KeyValuePair<string, string?>("ChampionsList:MinSynergyBaselineGames", "20"),
            ]);
}
