using Core.Lol.Lane;
using Data;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.Services.Champions.Matchups;

/// <summary>One of the player's games: their row and their lane opponent's, in one match.</summary>
internal readonly record struct PlayerLanePair(string MatchId, int SelfParticipantId, int OpponentParticipantId, int Opponent);

/// <summary>
/// One opponent's lane counters over the player's own games: wins and losses past the
/// threshold (evens in neither), and the gold and experience gaps summed over the lanes
/// they were measured on.
/// </summary>
internal readonly record struct PlayerLaneTally(int Wins, int Losses, long GoldDiffSum, long XpDiffSum, int Measured);

/// <summary>
/// The player-scoped matchups' lane half, judged live from the player's own games. The
/// aggregate's lane counters are folded over every tracked account, so they cannot be
/// lent to one player's row; this reads the same 15-minute snapshots the fold reads and
/// judges them with the same rule (<see cref="LaneOutcomeRules"/>), so "lane won" means
/// one thing on the champion page and on a player's.
/// </summary>
internal static class PlayerLaneOutcomes
{
    /// <summary>The mark both sides are compared at, matching the ingestor's fold.</summary>
    private const int LaneOutcomeMinute = 15;

    public static async Task<Dictionary<int, PlayerLaneTally>> TallyAsync(
        TrueMainDbContext db,
        IReadOnlyCollection<PlayerLanePair> pairs,
        CancellationToken ct)
    {
        var tallies = new Dictionary<int, PlayerLaneTally>();
        if (pairs.Count == 0)
        {
            return tallies;
        }

        // One player's games on one champion: a few hundred matches at most, so one
        // keyed read of their 15-minute readings, both sides of every lane at once.
        var matchIds = pairs.Select(p => p.MatchId).Distinct().ToList();
        var readings = await db.MatchParticipantTimelineSnapshots
            .AsNoTracking()
            .Where(s => matchIds.Contains(s.MatchId) && s.IntervalMinute == LaneOutcomeMinute)
            .Select(s => new { s.MatchId, s.ParticipantId, s.TotalGold, s.Xp })
            .ToDictionaryAsync(s => (s.MatchId, s.ParticipantId), s => (s.TotalGold, s.Xp), ct);

        foreach (var pair in pairs)
        {
            // A game that ended before 15 minutes, or whose timeline never arrived, is a
            // real game but not a lane anyone can call: it counts in the row's games and
            // in none of these counters — the fold's rule.
            if (!readings.TryGetValue((pair.MatchId, pair.SelfParticipantId), out var self)
                || !readings.TryGetValue((pair.MatchId, pair.OpponentParticipantId), out var opponent))
            {
                continue;
            }

            var gold = self.TotalGold - opponent.TotalGold;
            var tally = tallies.GetValueOrDefault(pair.Opponent);
            var standing = LaneOutcomeRules.Judge(gold, LaneOutcomeRules.DefaultGoldLeadThreshold);
            tallies[pair.Opponent] = tally with
            {
                Wins = tally.Wins + (standing == LaneStanding.Won ? 1 : 0),
                Losses = tally.Losses + (standing == LaneStanding.Lost ? 1 : 0),
                GoldDiffSum = tally.GoldDiffSum + gold,
                XpDiffSum = tally.XpDiffSum + (self.Xp - opponent.Xp),
                Measured = tally.Measured + 1,
            };
        }

        return tallies;
    }
}
