using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Core.Lol.Ranking;
using Data.Entities;
using Data.ItemContext;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Champions;

namespace TrueMain.IntegrationTests;

/// <summary>
/// End-to-end cover for the draft assistant (#1935): <c>POST /champions/draft</c> and
/// <c>POST /champions/draft/bans</c> against Postgres, through the query services, their
/// SQL and the patch fallback (#1923). The scoring arithmetic has unit tests of its own;
/// these pin what only the database can break — which rows are read, on which patch,
/// and which champions are filtered out before anything is scored.
/// </summary>
/// <remarks>
/// <para>
/// One seed serves every test, with numbers chosen so each case is readable by hand.
/// Every champion's record sits at exactly 50% at the lane and every <c>SELF</c>
/// baseline at 50%, so the strength term is zero for the main candidates and each
/// matchup delta is the raw win rate into the opponent minus one half.
/// </para>
/// <para>
/// Three patches are stored. <see cref="Current"/> and <see cref="Previous"/> are the
/// draft's window; <see cref="Stale"/> carries numbers that would visibly move every
/// assertion if a read leaked past the window, the "every stored patch" read #1906
/// removed.
/// </para>
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class ChampionDraftApiIntegrationTests : IAsyncLifetime
{
    private const string Position = "MIDDLE";
    private const string Current = "16.20";
    private const string Previous = "16.19";
    private const string Stale = "16.18";
    private const string Band = "GOLD";

    // Our candidates, all MIDDLE.
    private const int Ahri = 103;
    private const int LeBlanc = 7;
    private const int Annie = 1; // Thin on the current patch: falls back to the window.

    // Their lane opponents.
    private const int Zed = 238;
    private const int Fizz = 105; // The most played opponent on the lane by far.
    private const int Syndra = 134;
    private const int Kassadin = 38;

    // Off the lane.
    private const int Lulu = 117; // UTILITY
    private const int LeeSin = 64; // JUNGLE
    private const int Garen = 86; // TOP — heavily banned, never a mid ban suggestion.

    /// <summary>The champions every <c>ALLY</c> baseline row puts at <see cref="Position"/> on the current patch.</summary>
    private const double LaneGamesOnCurrent = 3_000 + 1_000 + 1_000 + 600 + 400 + 400 + 200;

    private static readonly string[] TextFreeStringFields = ["kind", "position", "axis", "bucket"];

    private readonly PostgresFixture _fixture;

    public ChampionDraftApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task PostDraftAsync_WithAnEnemyOnOurLane_RanksByTheMatchupIntoIt_AndKeepsTheShippedFields()
    {
        await SeedAsync();

        await using var factory = new TrueMainWebApplicationFactory<Program>(_fixture);
        using var client = CreateClient(factory);

        // Zed is unpinned: the solver places him from his lane prior, MIDDLE on every
        // stored patch.
        var response = await client.PostAsJsonAsync("/champions/draft", new
        {
            position = "middle",
            enemyChampions = new[] { Zed },
            candidates = new[] { LeBlanc, Ahri },
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        AssertShippedRecommendationFields(json);

        var draft = JsonSerializer.Deserialize<DraftRecommendationResponse>(json, JsonSerializerOptions.Web)!;
        draft.Position.Should().Be(Position);
        draft.LaneOpponentChampionId.Should().Be(Zed);
        draft.LaneOpponentProbability.Should().BeGreaterThan(0.99, "Zed is only ever played MIDDLE");
        draft.EnemyLanes.Should().ContainSingle(lane => lane.ChampionId == Zed && lane.Position == Position);

        // Ahri wins 60% into Zed (+10 pts over her 50%), LeBlanc 40% (−10 pts): the
        // enemy on the lane decides, whatever either does blind.
        draft.Candidates.Select(c => c.ChampionId).Should().Equal(Ahri, LeBlanc);
        var ahri = draft.Candidates[0];
        ahri.MatchupDelta.Should().BeApproximately(0.10, 1e-9);
        ahri.MatchupGames.Should().Be(400);
        ahri.StrengthDelta.Should().BeApproximately(0, 1e-9);
        ahri.StrengthGames.Should().Be(1_200);
        ahri.ThinSample.Should().BeFalse();
        ahri.Patch.Should().Be(Current);
        draft.Candidates[1].MatchupDelta.Should().BeApproximately(-0.10, 1e-9);

        // The matchup carries the pick; with no ally on the board, the lane phase into
        // the likely opponent fills the card (150 of 250 decided lanes won at 15 min).
        ahri.Reasons.Select(r => r.Kind).Should().Equal(DraftReasonKinds.LaneMatchup, DraftReasonKinds.LanePhase);
        var matchup = ahri.Reasons[0];
        matchup.ChampionId.Should().Be(Zed);
        matchup.Delta.Should().BeApproximately(0.10, 1e-9);
        matchup.Games.Should().Be(400);
        matchup.Probability.Should().BeGreaterThan(0.99);
        var lanePhase = ahri.Reasons[1];
        lanePhase.ChampionId.Should().Be(Zed);
        lanePhase.Rate.Should().BeApproximately(0.6, 1e-9);
        lanePhase.Games.Should().Be(250);
    }

    [Fact]
    public async Task PostDraftAsync_WithNoEnemyOnOurLane_RanksByBlindSafety()
    {
        await SeedAsync();

        await using var factory = new TrueMainWebApplicationFactory<Program>(_fixture);
        using var client = CreateClient(factory);

        var draft = await PostDraftAsync(client, new
        {
            position = Position,
            candidates = new[] { Ahri, LeBlanc },
        });

        draft.LaneOpponentChampionId.Should().BeNull("no enemy has picked yet");
        draft.LaneOpponentProbability.Should().Be(0);
        draft.EnemyLanes.Should().BeEmpty();

        // Blind, Fizz decides: he is half the lane's games, Ahri loses into him and
        // LeBlanc beats him — the reverse of the order with Zed on the lane.
        draft.Candidates.Select(c => c.ChampionId).Should().Equal(LeBlanc, Ahri);

        var ahri = draft.Candidates.Single(c => c.ChampionId == Ahri);
        ahri.MatchupDelta.Should().Be(0);
        ahri.MatchupGames.Should().Be(0);
        // Weighted by lane games: (1000 × +0.10 + 3000 × −0.10 + 1000 × +0.15 + 600 × −0.15) / 5600.
        ahri.Blind.Delta.Should().BeApproximately(-140d / 5_600d, 1e-9);
        ahri.Blind.Games.Should().Be(1_200);
        // Clearly behind (−2 pts once shrunk) into Fizz and Kassadin, of the six
        // other champions played on the lane.
        ahri.Blind.LosingInto.Should().Be(2);
        ahri.Blind.LikelyOpponents.Should().Be(6);

        var blind = ahri.Reasons.Should().ContainSingle(r => r.Kind == DraftReasonKinds.BlindSafety).Subject;
        blind.Delta.Should().BeApproximately(-140d / 5_600d, 1e-9);
        blind.Games.Should().Be(1_200);
        blind.Count.Should().Be(2);
        blind.Of.Should().Be(6);
        ahri.Reasons.Should().NotContain(r => r.Kind == DraftReasonKinds.LaneMatchup);

        var leblanc = draft.Candidates.Single(c => c.ChampionId == LeBlanc);
        leblanc.Blind.Delta.Should().BeApproximately(200d / 5_000d, 1e-9);
        leblanc.Blind.LosingInto.Should().Be(1, "only Zed is a clear loss for LeBlanc");
    }

    [Fact]
    public async Task PostDraftAsync_NeverSuggestsSpokenForChampions_AndCountsHoveredAlliesAsTentativeSynergy()
    {
        await SeedAsync();

        await using var factory = new TrueMainWebApplicationFactory<Program>(_fixture);
        using var client = CreateClient(factory);

        var draft = await PostDraftAsync(client, new
        {
            position = Position,
            enemyChampions = new[] { Zed },
            bans = new[] { Syndra },
            allies = new Dictionary<string, int> { ["UTILITY"] = Lulu },
            hoveredAllies = new Dictionary<string, int> { ["jungle"] = LeeSin },
            candidates = new[] { Ahri, Syndra, Zed, Lulu, LeeSin, LeBlanc },
        });

        // Banned, picked by the enemy, locked by an ally, hovered by an ally: none is ours.
        draft.Candidates.Select(c => c.ChampionId).Should().BeEquivalentTo([Ahri, LeBlanc]);

        var ahri = draft.Candidates.Single(c => c.ChampionId == Ahri);

        // The banned Syndra and the picked Zed are no longer possible lane opponents
        // either: the blind expectation keeps Fizz and Kassadin only.
        ahri.Blind.Games.Should().Be(600);
        ahri.Blind.Delta.Should().BeApproximately(-390d / 3_600d, 1e-9);

        // A mean, not a sum: Lulu +10 pts over 400 games at full weight, Lee Sin
        // +15 pts over 400 games at half weight, as he is only hovering.
        ahri.SynergyDelta.Should().BeApproximately((0.10 + (0.5 * 0.15)) / 1.5, 1e-9);
        ahri.SynergyGames.Should().Be(800);

        // Three score terms, then the lane phase into Zed, which is never one.
        ahri.Reasons.Select(r => r.Kind).Should().Equal(
            DraftReasonKinds.LaneMatchup, DraftReasonKinds.Synergy, DraftReasonKinds.Synergy, DraftReasonKinds.LanePhase);
        var lulu = ahri.Reasons.Single(r => r.Kind == DraftReasonKinds.Synergy && r.ChampionId == Lulu);
        lulu.Position.Should().Be("UTILITY");
        lulu.Delta.Should().BeApproximately(0.10, 1e-9);
        lulu.Games.Should().Be(400);
        lulu.Tentative.Should().BeFalse();
        var leeSin = ahri.Reasons.Single(r => r.Kind == DraftReasonKinds.Synergy && r.ChampionId == LeeSin);
        leeSin.Position.Should().Be("JUNGLE");
        leeSin.Delta.Should().BeApproximately(0.15, 1e-9);
        leeSin.Tentative.Should().BeTrue();

        draft.Candidates.Single(c => c.ChampionId == LeBlanc).SynergyGames
            .Should().Be(0, "LeBlanc has no measured pairing with either ally");
    }

    [Fact]
    public async Task PostDraftAsync_WithThreeAlliesOnTheBoard_StatesTheTeamComposition()
    {
        await SeedAsync();
        await SeedTeamFitAsync();

        await using var factory = new TrueMainWebApplicationFactory<Program>(_fixture);
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync("/champions/draft", new
        {
            position = Position,
            allies = new Dictionary<string, int> { ["JUNGLE"] = LeeSin, ["TOP"] = Garen },
            hoveredAllies = new Dictionary<string, int> { ["UTILITY"] = Lulu },
            candidates = new[] { Ahri, LeBlanc },
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        AssertTextFreeReasons(json);
        var draft = JsonSerializer.Deserialize<DraftRecommendationResponse>(json, JsonSerializerOptions.Web)!;

        // Three mostly physical allies, Garen the one frontline: AllyMagicDamage Low and
        // AllyFrontline Mid. Ahri wins 65% of 400 games with a physical team against 50%
        // overall (+15 pts); with one frontline, 50% of 500 (no effect). The stale patch's
        // rows and the boots slot's are never read.
        var ahri = draft.Candidates.Single(c => c.ChampionId == Ahri);
        ahri.TeamFitDelta.Should().BeApproximately(0.075, 1e-9);
        ahri.TeamFitGames.Should().Be(500);
        var fit = ahri.Reasons[^1];
        fit.Kind.Should().Be(DraftReasonKinds.TeamFit);
        fit.Axis.Should().Be("AllyMagicDamage");
        fit.Bucket.Should().Be("Low");
        fit.Delta.Should().BeApproximately(0.15, 1e-9);
        fit.Games.Should().Be(400);
        fit.Tentative.Should().BeTrue("Lulu is only hovering");

        // The composition is stated, not scored (weight 0 until the back-test sets it):
        // it comes after the terms that are.
        ahri.Reasons.Take(ahri.Reasons.Count - 1).Should().NotContain(r => r.Kind == DraftReasonKinds.TeamFit);
        draft.Candidates.Single(c => c.ChampionId == LeBlanc).TeamFitGames
            .Should().Be(0, "LeBlanc has no item-context totals");
    }

    [Fact]
    public async Task PostDraftAsync_ReadsTheCurrentPatch_AndFallsBackToThePreviousOnePerChampion()
    {
        await SeedAsync();

        await using var factory = new TrueMainWebApplicationFactory<Program>(_fixture);
        using var client = CreateClient(factory);

        var draft = await PostDraftAsync(client, new
        {
            position = Position,
            enemyChampions = new[] { Zed },
            pinnedEnemyLanes = new Dictionary<int, string> { [Zed] = "middle" },
            candidates = new[] { Ahri, Annie },
        });

        draft.Patch.Should().Be(Current, "no patch was requested: the newest stored one");
        draft.PreviousPatch.Should().Be(Previous);
        draft.LaneOpponentProbability.Should().Be(1, "a pinned enemy is certain");

        // Ahri has 1,200 games on the current patch, over the 200-game threshold: the
        // previous patch's 20% into Zed and the stale patch's 0% are not added.
        var ahri = draft.Candidates.Single(c => c.ChampionId == Ahri);
        ahri.Patch.Should().Be(Current);
        ahri.MatchupDelta.Should().BeApproximately(0.10, 1e-9);
        ahri.MatchupGames.Should().Be(400);
        ahri.StrengthGames.Should().Be(1_200);

        // Annie has 100: her record is the current and previous patches summed —
        // 260 wins of 400 into Zed (65%) against 300 of 500 overall (60%) — and
        // still not the stale patch's 1,000 lost games into him.
        var annie = draft.Candidates.Single(c => c.ChampionId == Annie);
        annie.Patch.Should().Be($"{Current}+{Previous}");
        annie.MatchupDelta.Should().BeApproximately(0.05, 1e-9);
        annie.MatchupGames.Should().Be(400);
        annie.StrengthGames.Should().Be(500);
        // 60% against a lane average of 50% over the window — the stale patch's 90%
        // SELF row would drag that average if it were read.
        annie.StrengthDelta.Should().BeApproximately(0.10, 1e-9);
    }

    [Fact]
    public async Task PostDraftBansAsync_RanksThreatsToTheDeclaredPick_ByMatchupAndPlayRate()
    {
        await SeedAsync();

        await using var factory = new TrueMainWebApplicationFactory<Program>(_fixture);
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync("/champions/draft/bans", new
        {
            position = Position,
            plannedPick = Ahri,
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        AssertTextFreeReasons(json);
        var bans = JsonSerializer.Deserialize<DraftBanResponse>(json, JsonSerializerOptions.Web)!;

        bans.Position.Should().Be(Position);
        bans.Patch.Should().Be(Current);
        bans.Target.Should().Be(DraftBanTargets.Pick);
        bans.TargetChampionIds.Should().Equal(Ahri);

        // Kassadin beats Ahri harder (−15 pts) than Fizz does (−10 pts), but Fizz is
        // played five times as often on the lane: he is the ban. Zed and Syndra lose
        // into Ahri and are no threat at all.
        bans.Candidates.Select(c => c.ChampionId).Should().Equal(Fizz, Kassadin);
        bans.Candidates.Should().OnlyContain(c => c.Score > 0);

        var fizz = bans.Candidates[0].Reasons.Should().ContainSingle().Subject;
        fizz.Kind.Should().Be(DraftReasonKinds.LaneThreat);
        fizz.ChampionId.Should().Be(Ahri);
        fizz.Delta.Should().BeApproximately(-0.10, 1e-9);
        fizz.Games.Should().Be(400);
        fizz.Share.Should().BeApproximately(3_000d / LaneGamesOnCurrent, 1e-9);
    }

    [Fact]
    public async Task PostDraftBansAsync_WithoutAPick_ProtectsTheMasteryPool_AndSkipsExcludedChampions()
    {
        await SeedAsync();

        await using var factory = new TrueMainWebApplicationFactory<Program>(_fixture);
        using var client = CreateClient(factory);

        var pool = new[]
        {
            new { championId = LeBlanc, weight = 100_000d },
            new { championId = Ahri, weight = 200_000d },
        };

        var open = await PostBansAsync(client, new { position = Position, pool });

        open.Target.Should().Be(DraftBanTargets.Pool);
        open.TargetChampionIds.Should().Equal(Ahri, LeBlanc);
        // Fizz and Kassadin threaten Ahri, two thirds of the pool's mastery; Zed only
        // threatens LeBlanc, the remaining third.
        open.Candidates.Select(c => c.ChampionId).Should().Equal(Fizz, Kassadin, Zed);
        open.Candidates[2].Reasons.Should().ContainSingle()
            .Which.ChampionId.Should().Be(LeBlanc, "Zed is the threat to LeBlanc, not to Ahri");

        // An ally hovering Fizz and Kassadin already banned: neither is suggested.
        var constrained = await PostBansAsync(client, new
        {
            position = Position,
            pool,
            allyChampions = new[] { Fizz },
            bans = new[] { Kassadin },
        });

        constrained.Target.Should().Be(DraftBanTargets.Pool);
        constrained.Candidates.Select(c => c.ChampionId).Should().Equal(Zed);
    }

    [Fact]
    public async Task PostDraftBansAsync_WithNoMeasurableThreat_FallsBackToTheLanesMostBannedChampions()
    {
        await SeedAsync();

        await using var factory = new TrueMainWebApplicationFactory<Program>(_fixture);
        using var client = CreateClient(factory);

        // Nothing to protect: the lane's most banned champions on the current patch,
        // the enemy's Zed aside. Garen is banned most of all but is no mid, and the
        // previous patch's LeBlanc-heavy bans are not this patch's.
        var nothing = await PostBansAsync(client, new
        {
            position = Position,
            enemyChampions = new[] { Zed },
        });

        nothing.Target.Should().Be(DraftBanTargets.None);
        nothing.TargetChampionIds.Should().BeEmpty();
        nothing.Candidates.Select(c => c.ChampionId).Should().Equal(Fizz, Ahri, Kassadin, Syndra, LeBlanc);
        nothing.Candidates.Should().OnlyContain(c => c.Score == 0);
        var fizz = nothing.Candidates[0].Reasons.Should().ContainSingle().Subject;
        fizz.Kind.Should().Be(DraftReasonKinds.BanRate);
        fizz.ChampionId.Should().Be(Fizz);
        fizz.Rate.Should().BeApproximately(0.4, 1e-9);
        fizz.Games.Should().Be(1_000);

        // A declared pick whose only threats an ally hovers or a ban removed: the
        // same fallback, and the pick itself — the second most banned mid — is
        // protected, never suggested.
        var cleared = await PostBansAsync(client, new
        {
            position = Position,
            plannedPick = Ahri,
            allyChampions = new[] { Fizz },
            bans = new[] { Kassadin },
        });

        cleared.Target.Should().Be(DraftBanTargets.Pick);
        cleared.Candidates.Select(c => c.ChampionId).Should().Equal(Zed, Syndra, LeBlanc, Annie);
        cleared.Candidates.Should().OnlyContain(c => c.Reasons.All(r => r.Kind == DraftReasonKinds.BanRate));
    }

    /// <summary>
    /// The fields the shipped desktop app reads (<c>desktop/app/app/types/draft.ts</c>)
    /// must stay in the payload until every installed app has moved on, and the
    /// reasons must stay data the client words, never a sentence.
    /// </summary>
    private static void AssertShippedRecommendationFields(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("position").ValueKind.Should().Be(JsonValueKind.String);
        root.TryGetProperty("patch", out _).Should().BeTrue();
        root.GetProperty("eloBracket").ValueKind.Should().Be(JsonValueKind.String);
        root.GetProperty("laneOpponentChampionId").ValueKind.Should().Be(JsonValueKind.Number);
        root.GetProperty("laneOpponentConfidence").ValueKind.Should().Be(JsonValueKind.Number);

        var lane = root.GetProperty("enemyLanes").EnumerateArray().Should().ContainSingle().Subject;
        foreach (var field in new[] { "championId", "position", "confidence", "pinned" })
        {
            lane.TryGetProperty(field, out _).Should().BeTrue($"enemyLanes[].{field} is read by the shipped app");
        }

        var candidates = root.GetProperty("candidates").EnumerateArray().ToList();
        candidates.Should().NotBeEmpty();
        foreach (var candidate in candidates)
        {
            foreach (var field in new[]
                     {
                         "championId", "matchupDelta", "matchupGames", "synergyDelta", "synergyGames", "score",
                     })
            {
                candidate.GetProperty(field).ValueKind
                    .Should().Be(JsonValueKind.Number, $"candidates[].{field} is read by the shipped app");
            }

            candidate.GetProperty("thinSample").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        }

        AssertTextFreeReasons(json);
    }

    /// <summary>
    /// Every reason is a kind and numbers: the only strings are codes — the kind, a lane,
    /// a composition axis and its band.
    /// </summary>
    private static void AssertTextFreeReasons(string json)
    {
        using var document = JsonDocument.Parse(json);
        var reasons = document.RootElement.GetProperty("candidates").EnumerateArray()
            .SelectMany(candidate => candidate.GetProperty("reasons").EnumerateArray())
            .ToList();
        reasons.Should().NotBeEmpty();

        foreach (var reason in reasons)
        {
            reason.GetProperty("kind").GetString().Should().NotBeNullOrEmpty();
            reason.EnumerateObject()
                .Where(property => property.Value.ValueKind == JsonValueKind.String)
                .Select(property => property.Name)
                .Should().BeSubsetOf(TextFreeStringFields, "a reason carries no wording of its own");
        }
    }

    private static async Task<DraftRecommendationResponse> PostDraftAsync(HttpClient client, object request)
    {
        var response = await client.PostAsJsonAsync("/champions/draft", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<DraftRecommendationResponse>())!;
    }

    private static async Task<DraftBanResponse> PostBansAsync(HttpClient client, object request)
    {
        var response = await client.PostAsJsonAsync("/champions/draft/bans", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<DraftBanResponse>())!;
    }

    /// <summary>
    /// The aggregates the draft reads, on three patches. See the class remarks for the
    /// shape; the per-test comments do the arithmetic.
    /// </summary>
    private async Task SeedAsync()
    {
        await using var db = _fixture.CreateDbContext();

        db.ChampionMatchupStats.AddRange(
            // Ahri on the current patch: 600 of 1,200 — +10 pts into Zed, −10 into
            // Fizz, +15 into Syndra, −15 into Kassadin.
            Matchup(Ahri, Zed, Current, games: 400, wins: 240, laneWins: 150, laneLosses: 100),
            Matchup(Ahri, Fizz, Current, games: 400, wins: 160),
            Matchup(Ahri, Syndra, Current, games: 200, wins: 130),
            Matchup(Ahri, Kassadin, Current, games: 200, wins: 70),
            // Ahri's previous patch, never read: the current one carries her.
            Matchup(Ahri, Zed, Previous, games: 300, wins: 60),
            Matchup(Ahri, Fizz, Previous, games: 300, wins: 240),
            Matchup(Ahri, Zed, Stale, games: 1_000, wins: 0),
            // LeBlanc, the mirror: −10 pts into Zed, +10 into Fizz, even into Syndra.
            Matchup(LeBlanc, Zed, Current, games: 400, wins: 160),
            Matchup(LeBlanc, Fizz, Current, games: 400, wins: 240),
            Matchup(LeBlanc, Syndra, Current, games: 200, wins: 100),
            // Annie: 100 games on the current patch, under the 200-game threshold.
            Matchup(Annie, Zed, Current, games: 100, wins: 50),
            Matchup(Annie, Zed, Previous, games: 300, wins: 210),
            Matchup(Annie, Fizz, Previous, games: 100, wins: 40),
            Matchup(Annie, Zed, Stale, games: 1_000, wins: 0));

        db.ChampionSynergyBaselineStats.AddRange(
            // SELF: our tracked players' own games, every one at 50% — the lane
            // average, and the synergy cohort.
            Baseline(Ahri, Position, SynergyBaselineSide.Self, Current, games: 1_200, wins: 600),
            Baseline(LeBlanc, Position, SynergyBaselineSide.Self, Current, games: 1_000, wins: 500),
            Baseline(Annie, Position, SynergyBaselineSide.Self, Current, games: 100, wins: 50),
            Baseline(Lulu, "UTILITY", SynergyBaselineSide.Self, Current, games: 1_000, wins: 500),
            Baseline(LeeSin, "JUNGLE", SynergyBaselineSide.Self, Current, games: 1_000, wins: 500),
            Baseline(Ahri, Position, SynergyBaselineSide.Self, Previous, games: 600, wins: 300),
            Baseline(Annie, Position, SynergyBaselineSide.Self, Previous, games: 400, wins: 200),
            Baseline(Ahri, Position, SynergyBaselineSide.Self, Stale, games: 1_000, wins: 900),
            // ALLY: every champion at the lane — the lane shares and the lane priors.
            Baseline(Fizz, Position, SynergyBaselineSide.Ally, Current, games: 3_000, wins: 1_500),
            Baseline(Zed, Position, SynergyBaselineSide.Ally, Current, games: 1_000, wins: 500),
            Baseline(Syndra, Position, SynergyBaselineSide.Ally, Current, games: 1_000, wins: 500),
            Baseline(Kassadin, Position, SynergyBaselineSide.Ally, Current, games: 600, wins: 300),
            Baseline(Ahri, Position, SynergyBaselineSide.Ally, Current, games: 400, wins: 200),
            Baseline(LeBlanc, Position, SynergyBaselineSide.Ally, Current, games: 400, wins: 200),
            Baseline(Annie, Position, SynergyBaselineSide.Ally, Current, games: 200, wins: 100),
            Baseline(Garen, "TOP", SynergyBaselineSide.Ally, Current, games: 1_000, wins: 500),
            Baseline(Zed, Position, SynergyBaselineSide.Ally, Previous, games: 1_000, wins: 500));

        db.ChampionSynergyStats.AddRange(
            // Ahri with Lulu: 60% against an expected 50%; with Lee Sin, 65%.
            Pair(Lulu, "UTILITY", Ahri, games: 400, wins: 240),
            Pair(LeeSin, "JUNGLE", Ahri, games: 400, wins: 260));

        db.BanScopeTotals.AddRange(
            BanTotal(Current, matches: 1_000),
            BanTotal(Previous, matches: 1_000));

        db.ChampionBanStats.AddRange(
            Bans(Garen, Current, 500),
            Bans(Fizz, Current, 400),
            Bans(Ahri, Current, 350),
            Bans(Zed, Current, 300),
            Bans(Kassadin, Current, 250),
            Bans(Syndra, Current, 200),
            Bans(LeBlanc, Current, 100),
            Bans(Annie, Current, 50),
            Bans(LeBlanc, Previous, 900));

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The composition term's inputs: the allies' profiles, and Ahri's item-context totals
    /// on the ally axes.
    /// </summary>
    private async Task SeedTeamFitAsync()
    {
        await using var db = _fixture.CreateDbContext();

        db.ChampionProfileStats.AddRange(
            Profile(LeeSin, "JUNGLE", magicShare: 0.1, tankRate: 0.1),
            Profile(Garen, "TOP", magicShare: 0, tankRate: 0.9),
            Profile(Lulu, "UTILITY", magicShare: 0.2, tankRate: 0));

        db.ChampionItemContextTotals.AddRange(
            Total(Current, ItemContextSlot.Build, ItemContextAxis.Overall, ItemContextBucket.All, games: 1_000, wins: 500),
            Total(Current, ItemContextSlot.Build, ItemContextAxis.AllyMagicDamage, ItemContextBucket.Low, games: 400, wins: 260),
            Total(Current, ItemContextSlot.Build, ItemContextAxis.AllyMagicDamage, ItemContextBucket.High, games: 300, wins: 60),
            Total(Current, ItemContextSlot.Build, ItemContextAxis.AllyFrontline, ItemContextBucket.Mid, games: 500, wins: 250),
            Total(Current, ItemContextSlot.Boots, ItemContextAxis.AllyMagicDamage, ItemContextBucket.Low, games: 900, wins: 0),
            Total(Stale, ItemContextSlot.Build, ItemContextAxis.AllyMagicDamage, ItemContextBucket.Low, games: 5_000, wins: 0));

        await db.SaveChangesAsync();
    }

    private static ChampionProfileStat Profile(int championId, string position, double magicShare, double tankRate)
        => new()
        {
            ChampionId = championId,
            Position = position,
            Patch = Current,
            Games = 1_000,
            GameDurationSecondsSum = 1_800_000,
            MagicDamageToChampionsSum = (long)(magicShare * 15_000_000),
            PhysicalDamageToChampionsSum = (long)((1 - magicShare) * 15_000_000),
            ItemGames = 1_000,
            TankGames = (int)(tankRate * 1_000),
            AggregatedAtUtc = DateTime.UtcNow,
        };

    private static ChampionItemContextTotal Total(
        string patch, ItemContextSlot slot, ItemContextAxis axis, ItemContextBucket bucket, int games, int wins)
        => new()
        {
            ChampionId = Ahri,
            Position = Position,
            Patch = patch,
            Slot = slot,
            ParentItemId = 0,
            Axis = axis,
            Bucket = bucket,
            Games = games,
            Wins = wins,
            AggregatedAtUtc = DateTime.UtcNow,
        };

    private static ChampionMatchupStat Matchup(
        int championId, int opponentChampionId, string patch, int games, int wins, int laneWins = 0, int laneLosses = 0)
        => new()
        {
            ChampionId = championId,
            TeamPosition = Position,
            OpponentChampionId = opponentChampionId,
            Patch = patch,
            EloBracket = Band,
            Games = games,
            Wins = wins,
            LaneGames = laneWins + laneLosses,
            LaneWins = laneWins,
            LaneLosses = laneLosses,
            AggregatedAtUtc = DateTime.UtcNow,
        };

    private static ChampionSynergyBaselineStat Baseline(
        int championId, string position, string side, string patch, int games, int wins)
        => new()
        {
            ChampionId = championId,
            TeamPosition = position,
            Side = side,
            Patch = patch,
            EloBracket = Band,
            Games = games,
            Wins = wins,
            AggregatedAtUtc = DateTime.UtcNow,
        };

    /// <summary>
    /// One pairing as the synergy read stores it: the ally is the queried champion,
    /// our candidate its partner at our lane.
    /// </summary>
    private static ChampionSynergyStat Pair(int allyChampionId, string allyPosition, int candidateId, int games, int wins)
        => new()
        {
            ChampionId = allyChampionId,
            TeamPosition = allyPosition,
            PartnerChampionId = candidateId,
            PartnerPosition = Position,
            Patch = Current,
            EloBracket = Band,
            Games = games,
            Wins = wins,
            AggregatedAtUtc = DateTime.UtcNow,
        };

    private static BanScopeTotal BanTotal(string patch, int matches)
        => new()
        {
            Patch = patch,
            EloBracket = EloBracket.All,
            Matches = matches,
            AggregatedAtUtc = DateTime.UtcNow,
        };

    private static ChampionBanStat Bans(int championId, string patch, int bans)
        => new()
        {
            ChampionId = championId,
            Patch = patch,
            EloBracket = EloBracket.All,
            Bans = bans,
            AggregatedAtUtc = DateTime.UtcNow,
        };

    private static HttpClient CreateClient(TrueMainWebApplicationFactory<Program> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });
}
