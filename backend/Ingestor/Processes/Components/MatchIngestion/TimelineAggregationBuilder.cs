using Data.Entities;
using Data.Repositories;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.MatchIngestion;

/// <summary>
/// Folds a match timeline directly into the powerspike/lead aggregates (issue #525
/// follow-up) instead of persisting a per-minute snapshot grid. Reconstructs each
/// participant's state at minute marks 1..30 in memory, pairs the champion side
/// with its lane opponent, and emits the additive contribution the ingestion
/// upserts:
/// <list type="bullet">
///   <item>lead totals (tracked champion side) per (champion, position, minute);</item>
///   <item>lead-spread variance moments (whole lane population) per minute;</item>
///   <item>event occurrences (tracked side): level 6/11/16 reach + first item buys.</item>
/// </list>
/// The grid never touches the database — only these aggregates do.
/// </summary>
internal static class TimelineAggregationBuilder
{
    private const int MaxIntervalMinute = 30;

    // A minute mark is only captured if a frame sits within half a minute of it,
    // so games that ended before a mark simply produce no state for it.
    private const int FrameMatchToleranceMs = 30_000;

    private static readonly int[] IntervalMinutes = [.. Enumerable.Range(1, MaxIntervalMinute)];

    private static readonly int[] LevelMilestones = [6, 11, 16];

    private static readonly HashSet<string> CanonicalPositions =
        new(StringComparer.Ordinal) { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };

    public static TimelineAggregateContribution Build(
        int queueId,
        string patch,
        IReadOnlyList<MatchParticipant> participants,
        MatchTimelineDto timeline)
    {
        var empty = new TimelineAggregateContribution([], [], []);
        if (timeline.Frames.Count == 0 || participants.Count == 0)
        {
            return empty;
        }

        var states = BuildMinuteStates(timeline);
        if (states.Count == 0)
        {
            return empty;
        }

        var itemFirstMinute = BuildItemFirstMinutes(timeline);
        var opponentByParticipant = BuildOpponents(participants);

        var sigma = new Dictionary<int, SigmaAcc>();
        var leads = new Dictionary<(int Champion, string Position, int Minute), LeadAcc>();
        var events = new Dictionary<(int Champion, string Position, string Type, int RefId), EventAcc>();

        foreach (var participant in participants)
        {
            if (!opponentByParticipant.TryGetValue(participant.ParticipantId, out var opponentId)
                || !states.TryGetValue(participant.ParticipantId, out var selfStates)
                || !states.TryGetValue(opponentId, out var oppStates))
            {
                continue;
            }

            var tracked = participant.RiotAccountId != null;

            foreach (var (minute, self) in selfStates)
            {
                if (!oppStates.TryGetValue(minute, out var opp))
                {
                    continue;
                }

                var goldDiff = self.Gold - opp.Gold;
                var dmgDiff = self.Damage - opp.Damage;

                // Sigma spans the whole lane population; iterating every participant
                // as the champion side covers both directions of each pairing, which
                // mirrors the symmetric STDDEV_SAMP the raw scan used to compute.
                var acc = sigma.TryGetValue(minute, out var s) ? s : default;
                acc.N += 1;
                acc.SumGold += goldDiff;
                acc.SumSqGold += (double)goldDiff * goldDiff;
                acc.SumDmg += dmgDiff;
                acc.SumSqDmg += (double)dmgDiff * dmgDiff;
                sigma[minute] = acc;

                if (!tracked)
                {
                    continue;
                }

                var leadKey = (participant.ChampionId, participant.TeamPosition, minute);
                var lead = leads.TryGetValue(leadKey, out var l) ? l : default;
                lead.Games += 1;
                lead.GoldDiff += goldDiff;
                lead.CsDiff += self.Cs - opp.Cs;
                lead.KillsDiff += self.Kills - opp.Kills;
                lead.LevelDiff += self.Level - opp.Level;
                lead.XpDiff += self.Xp - opp.Xp;
                lead.DamageDiff += dmgDiff;
                leads[leadKey] = lead;
            }

            // Events belong to the tracked champion side and, like the leads, only to
            // participants that have a lane opponent (the read pairs the same way).
            if (!tracked)
            {
                continue;
            }

            foreach (var milestone in LevelMilestones)
            {
                var reached = FirstMinuteAtLevel(selfStates, milestone);
                if (reached is not null)
                {
                    AddEvent(events, participant.ChampionId, participant.TeamPosition, "level", milestone, reached.Value);
                }
            }

            if (itemFirstMinute.TryGetValue(participant.ParticipantId, out var items))
            {
                foreach (var (itemId, minute) in items)
                {
                    AddEvent(events, participant.ChampionId, participant.TeamPosition, "item", itemId, minute);
                }
            }
        }

        return new TimelineAggregateContribution(
            leads.Select(kv => new TimelineLeadContribution(
                kv.Key.Champion, kv.Key.Position, patch, kv.Key.Minute,
                kv.Value.Games, kv.Value.GoldDiff, kv.Value.CsDiff, kv.Value.KillsDiff,
                kv.Value.LevelDiff, kv.Value.XpDiff, kv.Value.DamageDiff)).ToList(),
            sigma.Select(kv => new TimelineSigmaContribution(
                queueId, patch, kv.Key,
                kv.Value.N, kv.Value.SumGold, kv.Value.SumSqGold, kv.Value.SumDmg, kv.Value.SumSqDmg)).ToList(),
            events.Select(kv => new TimelinePowerspikeEventContribution(
                kv.Key.Champion, kv.Key.Position, patch, kv.Key.Type, kv.Key.RefId,
                kv.Value.Games, kv.Value.SumEventMinute)).ToList());
    }

    private static int? FirstMinuteAtLevel(IReadOnlyDictionary<int, State> states, int level)
    {
        int? reached = null;
        foreach (var (minute, state) in states)
        {
            if (state.Level >= level && (reached is null || minute < reached))
            {
                reached = minute;
            }
        }

        return reached;
    }

    private static void AddEvent(
        Dictionary<(int Champion, string Position, string Type, int RefId), EventAcc> events,
        int championId,
        string position,
        string type,
        int refId,
        int minute)
    {
        var key = (championId, position, type, refId);
        var acc = events.TryGetValue(key, out var e) ? e : default;
        acc.Games += 1;
        acc.SumEventMinute += minute;
        events[key] = acc;
    }

    // participantId -> minute -> reconstructed state at that mark.
    private static Dictionary<int, Dictionary<int, State>> BuildMinuteStates(MatchTimelineDto timeline)
    {
        var killTimestamps = new Dictionary<int, List<int>>();
        foreach (var timelineEvent in timeline.Events)
        {
            if (timelineEvent.KillerId is > 0
                && timelineEvent.Type.Equals("CHAMPION_KILL", StringComparison.OrdinalIgnoreCase))
            {
                Record(killTimestamps, timelineEvent.KillerId.Value, timelineEvent.TimestampMs);
            }
        }

        var states = new Dictionary<int, Dictionary<int, State>>();
        foreach (var minute in IntervalMinutes)
        {
            var frame = SelectFrame(timeline.Frames, minute * 60_000);
            if (frame is null)
            {
                continue;
            }

            foreach (var participantFrame in frame.ParticipantFrames)
            {
                if (!states.TryGetValue(participantFrame.ParticipantId, out var perMinute))
                {
                    perMinute = new Dictionary<int, State>();
                    states[participantFrame.ParticipantId] = perMinute;
                }

                perMinute[minute] = new State(
                    participantFrame.TotalGold,
                    participantFrame.MinionsKilled + participantFrame.JungleMinionsKilled,
                    CountUpTo(killTimestamps, participantFrame.ParticipantId, frame.TimestampMs),
                    participantFrame.Level,
                    participantFrame.Xp,
                    participantFrame.TotalDamageToChampions);
            }
        }

        return states;
    }

    // participantId -> itemId -> minute of its first purchase.
    private static Dictionary<int, Dictionary<int, int>> BuildItemFirstMinutes(MatchTimelineDto timeline)
    {
        var firstMsByParticipant = new Dictionary<int, Dictionary<int, int>>();
        foreach (var timelineEvent in timeline.Events)
        {
            if (timelineEvent.ParticipantId <= 0
                || timelineEvent.ItemId is not { } itemId
                || !timelineEvent.Type.Equals("ITEM_PURCHASED", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!firstMsByParticipant.TryGetValue(timelineEvent.ParticipantId, out var perItem))
            {
                perItem = new Dictionary<int, int>();
                firstMsByParticipant[timelineEvent.ParticipantId] = perItem;
            }

            if (!perItem.TryGetValue(itemId, out var existing) || timelineEvent.TimestampMs < existing)
            {
                perItem[itemId] = timelineEvent.TimestampMs;
            }
        }

        var result = new Dictionary<int, Dictionary<int, int>>();
        foreach (var (participantId, perItem) in firstMsByParticipant)
        {
            var minutes = new Dictionary<int, int>();
            foreach (var (itemId, timestampMs) in perItem)
            {
                minutes[itemId] = (int)Math.Round(timestampMs / 60_000.0, MidpointRounding.AwayFromZero);
            }

            result[participantId] = minutes;
        }

        return result;
    }

    // participantId -> its single lane opponent (canonical position, opposite team).
    // Off-position or ambiguous rows are skipped: they are never a real matchup.
    private static Dictionary<int, int> BuildOpponents(IReadOnlyList<MatchParticipant> participants)
    {
        var result = new Dictionary<int, int>();
        foreach (var participant in participants)
        {
            if (!CanonicalPositions.Contains(participant.TeamPosition))
            {
                continue;
            }

            var opponents = participants
                .Where(o => o.TeamPosition == participant.TeamPosition && o.TeamId != participant.TeamId)
                .ToList();
            if (opponents.Count == 1)
            {
                result[participant.ParticipantId] = opponents[0].ParticipantId;
            }
        }

        return result;
    }

    // Precondition: frames are ordered by ascending TimestampMs (Riot's guarantee),
    // so |delta| to a fixed target is V-shaped and we can stop past the minimum.
    private static MatchTimelineFrameDto? SelectFrame(List<MatchTimelineFrameDto> frames, int targetMs)
    {
        MatchTimelineFrameDto? best = null;
        var bestDelta = int.MaxValue;

        foreach (var frame in frames)
        {
            var delta = Math.Abs(frame.TimestampMs - targetMs);
            if (delta > bestDelta)
            {
                break;
            }

            bestDelta = delta;
            best = frame;
        }

        return bestDelta <= FrameMatchToleranceMs ? best : null;
    }

    private static void Record(Dictionary<int, List<int>> timestampsByParticipant, int participantId, int timestampMs)
    {
        if (!timestampsByParticipant.TryGetValue(participantId, out var timestamps))
        {
            timestamps = [];
            timestampsByParticipant[participantId] = timestamps;
        }

        timestamps.Add(timestampMs);
    }

    private static int CountUpTo(Dictionary<int, List<int>> timestampsByParticipant, int participantId, int timestampMs)
        => timestampsByParticipant.TryGetValue(participantId, out var timestamps)
            ? timestamps.Count(timestamp => timestamp <= timestampMs)
            : 0;

    private readonly record struct State(int Gold, int Cs, int Kills, int Level, int Xp, int Damage);

    private struct SigmaAcc
    {
        public long N;
        public double SumGold;
        public double SumSqGold;
        public double SumDmg;
        public double SumSqDmg;
    }

    private struct LeadAcc
    {
        public int Games;
        public long GoldDiff;
        public long CsDiff;
        public long KillsDiff;
        public long LevelDiff;
        public long XpDiff;
        public long DamageDiff;
    }

    private struct EventAcc
    {
        public int Games;
        public long SumEventMinute;
    }
}
