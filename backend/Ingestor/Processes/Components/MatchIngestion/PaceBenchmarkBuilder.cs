using Core.Lol.Pace;
using Core.Lol.Ranking;
using Data.Aggregation;
using Data.Entities;
using Data.Repositories;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.MatchIngestion;

/// <summary>
/// Turns one match timeline into pace benchmark bin increments (#1912): for every whole
/// minute the game reached, each laner's cumulative CS and gold earned, binned by
/// <see cref="PaceHistogram"/> and keyed by the lobby's tier. Pure — the per-minute values
/// live only in memory and leave as counters.
/// </summary>
internal static class PaceBenchmarkBuilder
{
    /// <summary>
    /// The bins one match adds, or nothing when it cannot be placed: a remake, a match
    /// without a patch, or a lobby with no tracked account ranked at game time.
    /// </summary>
    public static List<PaceBenchmarkKey> Build(
        PaceBenchmarkFoldClaim match,
        IReadOnlyCollection<MatchParticipant> participants,
        MatchTimelineDto timeline,
        IReadOnlyDictionary<Guid, List<(DateTime CapturedAtUtc, string? Tier)>> tierHistoryByAccount)
    {
        var keys = new List<PaceBenchmarkKey>();
        if (ChampionCohort.IsRemake(match.GameDurationSeconds) || string.IsNullOrEmpty(match.Patch))
        {
            return keys;
        }

        var tier = ResolveLobbyTier(participants, tierHistoryByAccount, match.GameStartTimeUtc);
        if (tier is null)
        {
            return keys;
        }

        var positionByParticipant = participants
            .Where(participant => ChampionCohort.IsCanonicalPosition(participant.TeamPosition))
            .ToDictionary(participant => participant.ParticipantId, participant => participant.TeamPosition);

        // A minute counts only if the game reached it: the last frame sits at the game's end,
        // and the frame tolerance would otherwise read it as the next whole minute.
        var lastMinute = Math.Min(PaceHistogram.MaxMinute, match.GameDurationSeconds / 60);
        for (var minute = 1; minute <= lastMinute; minute++)
        {
            var frame = TimelineSnapshotBuilder.SelectFrame(timeline.Frames, minute * 60_000);
            if (frame is null)
            {
                continue;
            }

            foreach (var participantFrame in frame.ParticipantFrames)
            {
                if (!positionByParticipant.TryGetValue(participantFrame.ParticipantId, out var position))
                {
                    continue;
                }

                var cs = participantFrame.MinionsKilled + participantFrame.JungleMinionsKilled;
                keys.Add(Key(match.Patch, tier, position, minute, PaceMetric.Cs, cs));
                keys.Add(Key(match.Patch, tier, position, minute, PaceMetric.GoldEarned, participantFrame.TotalGold));
            }
        }

        return keys;
    }

    /// <summary>
    /// The tier the whole lobby is counted at: each tracked participant's tier at game time
    /// (<see cref="EloBracketResolver"/>, the enrichment pass's rule), and the lower median
    /// of them when several tracked accounts disagree. <see langword="null"/> when no tracked
    /// account has a ranked capture — such a lobby has no tier to be a sample of.
    /// </summary>
    internal static string? ResolveLobbyTier(
        IReadOnlyCollection<MatchParticipant> participants,
        IReadOnlyDictionary<Guid, List<(DateTime CapturedAtUtc, string? Tier)>> tierHistoryByAccount,
        DateTime gameStartUtc)
    {
        var ladderIndexes = participants
            .Where(participant => participant.RiotAccountId is not null)
            .Select(participant => tierHistoryByAccount.TryGetValue(participant.RiotAccountId!.Value, out var history)
                ? EloBracketResolver.FromNearestSnapshot(history, gameStartUtc)
                : EloBracket.Unranked)
            .Select(band => IndexOf(EloBracket.Ladder, band))
            .Where(index => index >= 0)
            .Order()
            .ToList();

        return ladderIndexes.Count == 0
            ? null
            : EloBracket.Ladder[ladderIndexes[(ladderIndexes.Count - 1) / 2]];
    }

    /// <summary>Sums a batch's bin increments into one count per key, the shape the upsert takes.</summary>
    public static List<PaceBenchmarkCount> Count(IEnumerable<PaceBenchmarkKey> keys)
        => keys
            .GroupBy(key => key)
            .Select(group => new PaceBenchmarkCount(group.Key, group.LongCount()))
            .ToList();

    private static PaceBenchmarkKey Key(string patch, string tier, string position, int minute, PaceMetric metric, int value)
        => new(patch, tier, position, minute, metric, PaceHistogram.ToBucket(metric, value));

    private static int IndexOf(IReadOnlyList<string> ladder, string band)
    {
        for (var i = 0; i < ladder.Count; i++)
        {
            if (string.Equals(ladder[i], band, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
