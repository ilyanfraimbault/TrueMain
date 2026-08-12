using Core.Lol.Lane;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// How the lane went in <em>the games a recommendation was computed from</em> (#1117).
///
/// <para>
/// <b>Why this exists rather than reusing the matchup aggregate.</b> The matchup tool's
/// stat line describes one sample — games used, draft match, win rate — and its lane
/// cell used to come from <c>champion_matchup_stats</c> instead. Two populations behind
/// one line, and they disagree in a way readers notice: the aggregate's champion side is
/// mains-only (#1087) while a composition sample takes any pilot, so a matchup with
/// eight sampled games and no main among them showed a lane win rate of "—" beside a
/// games count of 8. The strip now asks the same games every cell asks.
/// </para>
///
/// <para>
/// <b>It is a bounded join, not the scan #606 moved away from.</b> The caller already
/// selected its games — at most <c>CompositionSearch:TopK</c> of them — and hands over
/// their (match, participant) keys, so this reads two snapshot rows per game. What #606
/// retired was a self-join over every retained match; this is a lookup by primary key
/// over tens of rows.
/// </para>
/// </summary>
public sealed class CompositionLaneOutcomeQueryService(
    TrueMainDbContext db,
    IOptions<CompositionSearchOptions> options)
    : ICompositionLaneOutcomeQueryService
{
    /// <summary>The mark both sides are compared at, matching the ingestor's fold.</summary>
    private const int LaneOutcomeMinute = 15;

    public async Task<CompositionLaneReadModel> GetAsync(
        string position,
        IReadOnlyList<CompositionMatchRef> matches,
        CancellationToken ct)
    {
        if (matches.Count == 0)
        {
            return CompositionLaneReadModel.Empty;
        }

        var matchIds = matches.Select(m => m.MatchId).Distinct().ToList();

        // Both sides of the lane in the selected matches: the sampled participant and
        // whoever stood opposite it. Filtered to the position up front so a match's
        // other four lanes never cross the wire.
        var laneRows = await db.MatchParticipants
            .AsNoTracking()
            .Where(p => matchIds.Contains(p.MatchId) && p.TeamPosition == position)
            .Select(p => new { p.MatchId, p.ParticipantId, p.TeamId })
            .ToListAsync(ct);

        var readings = await db.MatchParticipantTimelineSnapshots
            .AsNoTracking()
            .Where(s => matchIds.Contains(s.MatchId) && s.IntervalMinute == LaneOutcomeMinute)
            .Select(s => new { s.MatchId, s.ParticipantId, s.TotalGold, s.Xp })
            .ToDictionaryAsync(s => (s.MatchId, s.ParticipantId), s => (s.TotalGold, s.Xp), ct);

        var byMatch = laneRows.GroupBy(r => r.MatchId).ToDictionary(g => g.Key, g => g.ToList());

        var wins = 0;
        var losses = 0;
        var measured = 0;
        long goldSum = 0;
        long xpSum = 0;

        foreach (var match in matches)
        {
            if (!byMatch.TryGetValue(match.MatchId, out var sides))
            {
                continue;
            }

            var self = sides.FirstOrDefault(r => r.ParticipantId == match.ParticipantId);
            if (self is null)
            {
                continue;
            }

            var opponent = sides.FirstOrDefault(r => r.TeamId != self.TeamId);
            if (opponent is null)
            {
                continue;
            }

            // No 15-minute reading for one of the two: a real game, but not a lane
            // anyone can call. Left out of every counter rather than guessed at —
            // the same rule the fold applies, and the reason the measured count is
            // returned separately from the sample size.
            if (!readings.TryGetValue((match.MatchId, self.ParticipantId), out var selfReading)
                || !readings.TryGetValue((match.MatchId, opponent.ParticipantId), out var opponentReading))
            {
                continue;
            }

            measured++;
            goldSum += selfReading.TotalGold - opponentReading.TotalGold;
            xpSum += selfReading.Xp - opponentReading.Xp;

            switch (LaneOutcomeRules.Judge(
                selfReading.TotalGold - opponentReading.TotalGold, options.Value.LaneGoldLeadThreshold))
            {
                case LaneStanding.Won:
                    wins++;
                    break;
                case LaneStanding.Lost:
                    losses++;
                    break;
            }
        }

        var decided = wins + losses;

        return new CompositionLaneReadModel
        {
            MeasuredGames = measured,
            DecidedGames = decided,
            // Decided lanes only, and null rather than 0 when none were: "no lane was
            // ever settled here" and "the lane is always lost" must not render alike.
            // No games floor on top of that — this strip states its own denominator in
            // the cell, and a floor would blank the figure on exactly the thin drafts
            // the tool exists to answer.
            WinRate = decided > 0 ? (double)wins / decided : null,
            AverageGoldDiffAt15 = measured > 0 ? (double)goldSum / measured : null,
            AverageXpDiffAt15 = measured > 0 ? (double)xpSum / measured : null,
        };
    }
}
