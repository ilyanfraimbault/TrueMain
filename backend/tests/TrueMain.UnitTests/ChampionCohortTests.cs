using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Data.Aggregation;
using Ingestor.Processes.Components.PatternAggregation;

namespace TrueMain.UnitTests;

/// <summary>
/// The parts of <see cref="ChampionCohort"/> that need no database — the rules a fold
/// asks about in memory — plus the guards that keep the folds and the live reads from
/// growing a cohort filter of their own again.
/// </summary>
public sealed class ChampionCohortTests
{
    /// <summary>
    /// The folds that write the panels stacked on one champion page. They must express
    /// their cohort through <see cref="ChampionCohort"/> and nowhere else: #1087 fixed
    /// the matchup folds and #1365 the synergy fold, each time because a fold had restated
    /// the rule in its own words and drifted. The lane fold was a fourth file until
    /// #1445 merged it into the matchup one.
    /// </summary>
    private static readonly string[] FoldSourceFiles =
    [
        "ChampionMatchupLeadAggregationProcess.cs",
        "ChampionSynergyAggregationProcess.cs",
        "ChampionOpponentAggregationProcess.cs"
    ];

    /// <summary>
    /// The API reads that compute a champion panel live from <c>match_participants</c>
    /// rather than from an aggregate, relative to <c>backend/</c>. Each restated the
    /// cohort in its own words until #1365 — "a tracked account", with or without
    /// <c>IsActive</c>, remakes counted — so one champion, lane and patch could carry
    /// several denominators on the same page.
    /// </summary>
    private static readonly string[] CohortReadSourceFiles =
    [
        "Api/Services/Champions/Builds/ChampionItemTimingsQueryService.cs",
        "Api/Services/Champions/Progression/ChampionScalingQueryService.cs",
        "Api/Services/Champions/Synergies/ChampionSynergyQueryService.cs",
        "Api/Services/Champions/Mains/ChampionMainsComparisonQueryService.cs"
    ];

    /// <summary>
    /// Live reads that deliberately choose another champion side — one player's own games,
    /// or the full pool a build is valid over — but still count games only, never a
    /// remake: they take their matches from <see cref="ChampionCohort.Games"/>.
    /// </summary>
    private static readonly string[] GameReadSourceFiles =
    [
        "Api/Services/Champions/Matchups/ChampionMatchupQueryService.cs",
        "Api/Services/Champions/Composition/CompositionMatchQueryService.cs",
        "Data/Queries/MatchupParticipantQuery.cs"
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(299)]
    public void A_game_under_five_minutes_is_a_remake(int durationSeconds)
        => ChampionCohort.IsRemake(durationSeconds).Should().BeTrue();

    [Theory]
    [InlineData(300)]
    [InlineData(301)]
    [InlineData(1800)]
    public void A_game_at_or_over_five_minutes_is_a_game(int durationSeconds)
        => ChampionCohort.IsRemake(durationSeconds).Should().BeFalse();

    [Theory]
    [InlineData("TOP")]
    [InlineData("JUNGLE")]
    [InlineData("MIDDLE")]
    [InlineData("BOTTOM")]
    [InlineData("UTILITY")]
    public void The_five_riot_lanes_are_canonical(string position)
        => ChampionCohort.IsCanonicalPosition(position).Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bottom")]
    [InlineData("SUPPORT")]
    [InlineData(null)]
    public void Anything_else_is_not_a_lane_anybody_can_ask_for(string? position)
        => ChampionCohort.IsCanonicalPosition(position).Should().BeFalse();

    [Fact]
    public void An_empty_batch_asks_nothing_of_the_database()
    {
        ChampionCohortSnapshot.Empty.Count.Should().Be(0);
        ChampionCohortSnapshot.Empty.IncludesMatch("EUW1_1").Should().BeFalse();
        ChampionCohortSnapshot.Empty.Includes("EUW1_1", 1).Should().BeFalse();
    }

    [Fact]
    public void Membership_is_per_match_because_a_participant_id_is_only_a_slot_number()
    {
        var snapshot = new ChampionCohortSnapshot(
            [new ChampionCohortKey("EUW1_1", 3)],
            new HashSet<string>(["EUW1_1", "EUW1_2"], StringComparer.Ordinal));

        snapshot.Includes("EUW1_1", 3).Should().BeTrue();
        snapshot.Includes("EUW1_2", 3).Should().BeFalse("slot 3 is a different player in a different game");
        snapshot.IncludesMatch("EUW1_2").Should().BeTrue("the match is still a game, it just has no cohort member");
    }

    /// <summary>
    /// The header's own floor is stricter than the shared remake rule and must stay that
    /// way: if it ever dropped below, the aggregate would count games the folds beside it
    /// throw away, which is the denominator mismatch this whole line of work exists to
    /// close.
    /// </summary>
    [Fact]
    public void The_header_floor_never_admits_a_game_the_shared_rule_rejects()
        => ChampionPatternSourceRowReader.MinimumAggregatedGameDurationSeconds
            .Should().BeGreaterThanOrEqualTo(ChampionCohort.MinimumGameDurationSeconds);

    /// <summary>
    /// A grep, deliberately: the defect this guards against is not a wrong answer, it is
    /// a plausible-looking line added to one fold — <c>RiotAccountId != null</c>, a local
    /// <c>IsMain</c> join, a private copy of the canonical positions — that quietly makes
    /// that panel count a different population from the four beside it. Nothing about the
    /// fold's output would look wrong; only the comparison with the header would, and
    /// that is what nobody re-runs. The source is located from this file's own compile
    /// time path, so it does not depend on the working directory the tests run from.
    /// </summary>
    [Fact]
    public void No_fold_expresses_its_own_cohort_filter()
    {
        var processes = FoldSourceDirectory();

        foreach (var file in FoldSourceFiles)
        {
            var path = Path.Combine(processes, file);
            File.Exists(path).Should().BeTrue($"{file} is one of the folds that must compose the shared cohort");

            var source = File.ReadAllText(path);

            source.Should().Contain(
                "ChampionCohort",
                $"{file} must take its cohort from Data.Aggregation.ChampionCohort");
            source.Should().NotContain(
                "RiotAccountId",
                $"{file} would be gating on \"an account we know\" instead of \"a main of this champion\" (#1087)");
            source.Should().NotContain(
                "IsMain",
                $"{file} would be restating the cohort join instead of composing it");
            source.Should().NotContain(
                "\"TOP\", \"JUNGLE\"",
                $"{file} would be keeping a private copy of ChampionCohort.CanonicalPositions");
            source.Should().NotContain(
                "GameDurationSeconds",
                $"{file} would be deciding for itself what a remake is (ChampionCohort.MinimumGameDurationSeconds)");
        }
    }

    /// <summary>
    /// The same grep for the live reads, for the same reason: a read that says
    /// <c>RiotAccountId != null</c> answers a different question from the header above it
    /// and nothing on the page would show it.
    /// </summary>
    [Fact]
    public void No_live_champion_read_expresses_its_own_cohort_filter()
    {
        foreach (var file in CohortReadSourceFiles)
        {
            var source = ReadBackendSource(file);

            source.Should().Contain(
                "ChampionCohort.Members",
                $"{file} must take its champion side from Data.Aggregation.ChampionCohort");
            source.Should().NotContain(
                "RiotAccountId != null",
                $"{file} would be counting \"an account we know\" instead of the cohort (#1087, #1365)");
            source.Should().NotContain(
                "\"RiotAccountId\" IS NOT NULL",
                $"{file} would be restating the cohort in its SQL");
            source.Should().NotContain(
                "IsMain",
                $"{file} would be restating the cohort join instead of composing it");
            source.Should().NotContain(
                "\"TOP\", \"JUNGLE\"",
                $"{file} would be keeping a private copy of ChampionCohort.CanonicalPositions");
            source.Should().NotContain(
                "db.Matches",
                $"{file} would be choosing its matches without the remake rule (ChampionCohort.Games)");
        }
    }

    [Fact]
    public void No_live_champion_read_counts_a_remake()
    {
        foreach (var file in GameReadSourceFiles)
        {
            var source = ReadBackendSource(file);

            source.Should().Contain(
                "ChampionCohort.Games",
                $"{file} must take its matches from ChampionCohort.Games");
            source.Should().NotContain(
                "db.Matches",
                $"{file} would be choosing its matches without the remake rule");
            source.Should().NotContain(
                "GameDurationSeconds >",
                $"{file} would be deciding for itself what a remake is (ChampionCohort.MinimumGameDurationSeconds)");
        }
    }

    private static string ReadBackendSource(string relativePath)
    {
        var path = Path.Combine(BackendDirectory(), relativePath);
        File.Exists(path).Should().BeTrue($"{relativePath} is a live champion read that must compose the shared cohort");
        return File.ReadAllText(path);
    }

    private static string BackendDirectory()
    {
        // <repo>/backend/tests/TrueMain.UnitTests/ChampionCohortTests.cs
        var testsProject = Path.GetDirectoryName(ThisFilePath())!;
        return Path.GetFullPath(Path.Combine(testsProject, "..", ".."));
    }

    private static string FoldSourceDirectory()
        => Path.Combine(BackendDirectory(), "Ingestor", "Processes");

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
