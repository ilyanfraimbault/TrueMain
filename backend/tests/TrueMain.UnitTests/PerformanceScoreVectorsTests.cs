using System.Text.Json;
using AwesomeAssertions;
using Core.Lol.Performance;
using TrueMain.Services.Truemains.PlayerChampions;

namespace TrueMain.UnitTests;

/// <summary>
/// Runs the vectors the desktop app's TypeScript port of the score runs too
/// (<c>web/shared/fixtures/performance-score-vectors.json</c>, #2075): the
/// dashboard grades the games only the player's client knows with that port,
/// so a game TrueMain also holds must come out with the same score, placement
/// and accolade from both. A figure changed on one side alone fails here or in
/// the app's suite.
/// </summary>
public sealed class PerformanceScoreVectorsTests
{
    private static readonly Lazy<Vectors> Shared = new(() => JsonSerializer.Deserialize<Vectors>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "performance-score-vectors.json")),
        JsonSerializerOptions.Web)!);

    public static TheoryData<string> ScoreNames() => [.. Shared.Value.Scores.Select(v => v.Name)];

    public static TheoryData<string> MatchNames() => [.. Shared.Value.Matches.Select(v => v.Name)];

    [Theory]
    [MemberData(nameof(ScoreNames))]
    public void Compute_GivesTheSharedScore(string name)
    {
        var vector = Shared.Value.Scores.Single(v => v.Name == name);

        PerformanceScore.Compute(vector.Input).Should().Be(vector.Score);
    }

    [Theory]
    [MemberData(nameof(MatchNames))]
    public void AMatch_IsGradedAndRankedAsShared(string name)
    {
        var vector = Shared.Value.Matches.Single(v => v.Name == name);
        var marks = vector.Marks.ToDictionary(m => (m.ParticipantId, m.Minute));

        var placements = MatchPerformanceRanker.Rank(PerformanceInputs
            .BuildMatchInputs(vector.Participants, vector.DurationSeconds, marks)
            .Select(built => new MatchPerformanceEntry
            {
                ParticipantId = built.Participant.ParticipantId,
                Win = built.Participant.Win,
                Score = PerformanceScore.Compute(built.Input),
                Kills = built.Participant.Kills,
                Deaths = built.Participant.Deaths,
                Assists = built.Participant.Assists,
            }));

        placements.Values.OrderBy(p => p.Placement).Should().Equal(vector.Placements);
    }

    [Fact]
    public void TheVectorsCoverEveryRoleAndSeveralMatches()
    {
        var positions = Shared.Value.Scores.Select(v => v.Input.TeamPosition.Trim().ToUpperInvariant()).ToHashSet();

        positions.Should().Contain(["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY", ""]);
        Shared.Value.Matches.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    private sealed record Vectors(IReadOnlyList<ScoreVector> Scores, IReadOnlyList<MatchVector> Matches);

    private sealed record ScoreVector(string Name, PerformanceScoreInput Input, int Score);

    private sealed record MatchVector(
        string Name,
        int DurationSeconds,
        IReadOnlyList<ScoredParticipant> Participants,
        IReadOnlyList<TimelineMark> Marks,
        IReadOnlyList<MatchPerformancePlacement> Placements);
}
