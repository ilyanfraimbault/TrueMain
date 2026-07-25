using AwesomeAssertions;
using Core.Lol.Performance;

namespace TrueMain.UnitTests;

public sealed class MatchPerformanceRankerTests
{
    private static MatchPerformanceEntry Entry(
        int participantId,
        bool win,
        int score,
        int kills = 0,
        int deaths = 0,
        int assists = 0)
        => new()
        {
            ParticipantId = participantId,
            Win = win,
            Score = score,
            Kills = kills,
            Deaths = deaths,
            Assists = assists,
        };

    /// <summary>A full 5v5 where blue (ids 1-5) won; scores are deliberately unsorted.</summary>
    private static List<MatchPerformanceEntry> FullMatch() =>
    [
        Entry(1, win: true, 61, kills: 4, deaths: 5, assists: 9),
        Entry(2, win: true, 74, kills: 8, deaths: 3, assists: 12),
        Entry(3, win: true, 88, kills: 12, deaths: 2, assists: 8),
        Entry(4, win: true, 70, kills: 9, deaths: 4, assists: 6),
        Entry(5, win: true, 66, kills: 1, deaths: 4, assists: 20),
        Entry(6, win: false, 52, kills: 3, deaths: 7, assists: 5),
        Entry(7, win: false, 45, kills: 2, deaths: 9, assists: 6),
        Entry(8, win: false, 79, kills: 11, deaths: 5, assists: 4),
        Entry(9, win: false, 58, kills: 6, deaths: 8, assists: 3),
        Entry(10, win: false, 40, kills: 0, deaths: 10, assists: 8),
    ];

    [Fact]
    public void Rank_assigns_a_strict_1_to_N_placement_ordered_by_score()
    {
        var placements = MatchPerformanceRanker.Rank(FullMatch());

        placements.Should().HaveCount(10);
        placements.Values.Select(p => p.Placement).Order()
            .Should().Equal(Enumerable.Range(1, 10));

        // 88 (id 3) → 1st, 79 (id 8) → 2nd, 74 (id 2) → 3rd, 40 (id 10) → 10th.
        placements[3].Placement.Should().Be(1);
        placements[8].Placement.Should().Be(2);
        placements[2].Placement.Should().Be(3);
        placements[10].Placement.Should().Be(10);
    }

    [Fact]
    public void Rank_marks_the_best_winner_as_MVP_and_the_best_loser_as_ACE()
    {
        var placements = MatchPerformanceRanker.Rank(FullMatch());

        placements.Values.Where(p => p.IsMvp).Select(p => p.ParticipantId)
            .Should().Equal(3);
        placements.Values.Where(p => p.IsAce).Select(p => p.ParticipantId)
            .Should().Equal(8);
    }

    [Fact]
    public void Rank_can_place_the_ACE_above_most_of_the_winning_side()
    {
        // A hard-carrying loser is allowed to out-place four of the five
        // winners — the score grades the individual, not the outcome.
        var placements = MatchPerformanceRanker.Rank(FullMatch());

        placements[8].Placement.Should().BeLessThan(placements[2].Placement);
        placements[8].IsMvp.Should().BeFalse();
    }

    [Fact]
    public void Rank_is_independent_of_the_input_order()
    {
        var forward = MatchPerformanceRanker.Rank(FullMatch());
        var reversed = MatchPerformanceRanker.Rank(Enumerable.Reverse(FullMatch()));

        foreach (var (participantId, placement) in forward)
        {
            reversed[participantId].Should().Be(placement);
        }
    }

    [Fact]
    public void Rank_breaks_score_ties_on_takedowns_then_deaths_then_participant_id()
    {
        var tied = new[]
        {
            // Same score: 12 takedowns beats 10, and on 10 takedowns the
            // 2-death line beats the 6-death one. The last pair is identical
            // apart from the id, which is the final, always-total tiebreak.
            Entry(4, win: true, 70, kills: 5, deaths: 6, assists: 5),
            Entry(1, win: true, 70, kills: 6, deaths: 3, assists: 6),
            Entry(3, win: true, 70, kills: 5, deaths: 2, assists: 5),
            Entry(2, win: true, 70, kills: 5, deaths: 2, assists: 5),
        };

        var placements = MatchPerformanceRanker.Rank(tied);

        placements[1].Placement.Should().Be(1);
        placements[2].Placement.Should().Be(2);
        placements[3].Placement.Should().Be(3);
        placements[4].Placement.Should().Be(4);
    }

    [Fact]
    public void Rank_marks_no_ACE_when_every_participant_won()
    {
        var placements = MatchPerformanceRanker.Rank(
        [
            Entry(1, win: true, 70),
            Entry(2, win: true, 50),
        ]);

        placements[1].IsMvp.Should().BeTrue();
        placements.Values.Should().OnlyContain(p => !p.IsAce);
    }

    [Fact]
    public void Rank_returns_nothing_for_an_empty_match()
    {
        MatchPerformanceRanker.Rank([]).Should().BeEmpty();
    }

    [Fact]
    public void Rank_rejects_a_null_sequence()
    {
        var act = () => MatchPerformanceRanker.Rank(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
