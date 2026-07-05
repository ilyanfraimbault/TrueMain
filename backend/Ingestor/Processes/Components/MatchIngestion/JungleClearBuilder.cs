using Core.Lol.Map;
using Data.Entities;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.MatchIngestion;

/// <summary>
/// Reconstructs each jungler's <b>first clear</b> from a match timeline (issue #535)
/// and emits one compact <see cref="JungleFirstClear"/> per jungler.
///
/// Riot emits no "camp killed" event for the small camps, so the only signal is the
/// once-per-minute <c>participantFrames</c> giving each participant's (x, y) plus
/// cumulative <c>jungleMinionsKilled</c>. The camp order is therefore <b>inferred</b>
/// from the per-minute position trail — the standard accepted method, reliable for
/// the first clear (~1 camp/min). At each early frame the jungler's position is
/// mapped to the nearest camp (<see cref="JungleCamps.NearestCamp"/>); the
/// <c>jungleMinionsKilled</c> delta confirms a camp was actually taken and lets us
/// stop at a full clear. Per-camp timing is minute resolution (the frame timestamp),
/// not exact.
///
/// Out of scope (issue #535): early-gank detection, per-champion aggregates, any
/// pathing past the first clear, and exact per-camp timestamps.
/// </summary>
internal static class JungleClearBuilder
{
    // A jungler clears ~1 camp/min; a full first clear (6 camps) plus scuttle/buffer
    // is done well inside this window. Frames past it add mid-game noise (multiple
    // camps/min, erratic movement) that the inference is explicitly not reliable for.
    internal const int FirstClearWindowMs = 8 * 60_000;

    // A participant is only treated as a jungler if their jungle CS grows by at least
    // this much across the window — filters laners who poke a single camp.
    internal const int MinJungleCsForJungler = 4;

    public static List<JungleFirstClear> Build(string matchId, MatchTimelineDto timeline)
    {
        var result = new List<JungleFirstClear>();
        if (timeline.Frames.Count == 0)
        {
            return result;
        }

        // Frames are ascending by TimestampMs (Riot guarantee). Restrict to the
        // first-clear window; the inference is only reliable here.
        var frames = timeline.Frames
            .Where(frame => frame.TimestampMs <= FirstClearWindowMs)
            .OrderBy(frame => frame.TimestampMs)
            .ToList();
        if (frames.Count == 0)
        {
            return result;
        }

        foreach (var participantId in IdentifyJunglers(frames))
        {
            var clear = BuildClear(matchId, participantId, frames);
            if (clear.Steps.Count > 0)
            {
                result.Add(clear);
            }
        }

        return result;
    }

    // Identify junglers from the frames alone: a participant whose jungle CS grows by
    // at least MinJungleCsForJungler over the window is a jungler. Riot's per-team
    // single-jungler role isn't needed — anyone who actually clears camps qualifies,
    // and laners who never touch the jungle are filtered by the threshold.
    private static IEnumerable<int> IdentifyJunglers(List<MatchTimelineFrameDto> frames)
    {
        var firstJungleCs = new Dictionary<int, int>();
        var lastJungleCs = new Dictionary<int, int>();

        foreach (var frame in frames)
        {
            foreach (var participantFrame in frame.ParticipantFrames)
            {
                firstJungleCs.TryAdd(participantFrame.ParticipantId, participantFrame.JungleMinionsKilled);
                lastJungleCs[participantFrame.ParticipantId] = participantFrame.JungleMinionsKilled;
            }
        }

        return lastJungleCs
            .Where(kvp => kvp.Value - firstJungleCs[kvp.Key] >= MinJungleCsForJungler)
            .Select(kvp => kvp.Key)
            .OrderBy(participantId => participantId);
    }

    private static JungleFirstClear BuildClear(string matchId, int participantId, List<MatchTimelineFrameDto> frames)
    {
        var steps = new List<JungleClearStep>();
        var previousJungleCs = (int?)null;
        int? fullClearTimeMs = null;
        IReadOnlyList<JungleCamp>? clearSet = null;
        var clearedFirstClearCamps = new HashSet<JungleCamp>();

        foreach (var frame in frames)
        {
            var participantFrame = frame.ParticipantFrames
                .FirstOrDefault(pf => pf.ParticipantId == participantId);
            if (participantFrame is null || participantFrame.X is not { } x || participantFrame.Y is not { } y)
            {
                continue;
            }

            // No new camp credit unless jungle CS actually advanced since the last
            // frame — disambiguates standing near a camp (recall, pathing through)
            // from clearing it, and skips minutes where the jungler ganked instead.
            var jungleCs = participantFrame.JungleMinionsKilled;
            var advanced = previousJungleCs is { } previous && jungleCs > previous;
            previousJungleCs ??= jungleCs;
            if (!advanced)
            {
                previousJungleCs = jungleCs;
                continue;
            }

            previousJungleCs = jungleCs;

            // Dedup against every camp already credited to this clear, not just the
            // immediately previous one: adjacent camps sit as little as ~1200 units
            // apart (inside the assignment radius), so a later frame's position noise
            // can map back onto an already-cleared, non-consecutive camp. Recording it
            // again would insert a duplicate step and corrupt the persisted sequence.
            var camp = JungleCamps.NearestCamp(x, y);
            if (camp == JungleCamp.Unknown || clearedFirstClearCamps.Contains(camp) || !JungleCamps.IsFirstClearCamp(camp))
            {
                continue;
            }

            // Pin the clear set to the side of the first camp the jungler takes, so a
            // mid-clear scuttle/cross-map detour can't flip which six camps count.
            clearSet ??= JungleCamps.BlueSideCamps.Contains(camp)
                ? JungleCamps.BlueSideCamps
                : JungleCamps.RedSideCamps;

            if (!clearSet.Contains(camp))
            {
                continue;
            }

            steps.Add(new JungleClearStep { Camp = camp.ToString(), TimestampMs = frame.TimestampMs });
            clearedFirstClearCamps.Add(camp);

            if (fullClearTimeMs is null && clearedFirstClearCamps.Count == clearSet.Count)
            {
                fullClearTimeMs = frame.TimestampMs;
                break; // first clear done — anything past here is out of scope (#535)
            }
        }

        return new JungleFirstClear
        {
            MatchId = matchId,
            ParticipantId = participantId,
            Steps = steps,
            FullClearTimeMs = fullClearTimeMs
        };
    }
}
