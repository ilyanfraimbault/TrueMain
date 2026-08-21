using Core.Lol.Map;
using Data.Entities;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.MatchIngestion;

/// <summary>
/// Measures each jungler's <b>first clear</b> from a match timeline (#1188,
/// replacing the camp-sequence reconstruction of #535).
///
/// <para><b>Why there is no camp order here.</b> Riot emits no camp-kill event,
/// and <c>participantFrames</c> are sampled once per <b>minute</b>. Buffs spawn
/// at 1:30 and the median jungler is at 12 jungle CS by minute 2 and 20 — a full
/// clear — by minute 3: three to four camps fall inside a single frame. The old
/// builder credited one camp per frame, which made a six-camp clear impossible to
/// report before 6:00 (production floor was exactly 6:00, average 6:59, and only
/// 0.06% of rows ever reached six camps). Two position samples cannot order six
/// camps, so the sequence is not reconstructable and is no longer claimed.</para>
///
/// <para>What is measurable, and what this emits: the camp the jungler opened on
/// (he waits on it while jungle CS is still 0), the per-minute clear-speed
/// samples, and the frame at which jungle CS reaches a full clear's worth.</para>
/// </summary>
internal static class JungleClearBuilder
{
    // Buffs spawn at 1:30 and a clear runs to ~3:15; minute 5 leaves room for a
    // slow or interrupted clear without dragging in mid-game rotations (the old
    // 8-minute window is what let normal mid-game backs read as clear events).
    internal const int FirstClearWindowMs = 5 * 60_000;

    // A participant is only treated as a jungler if their jungle CS grows by at
    // least this much across the window — filters laners who poke a camp. A
    // jungler counter-jungled out of their own jungle who never gets there yields
    // no row rather than a misleading one.
    internal const int MinJungleCsForJungler = 4;

    public static List<JungleFirstClear> Build(string matchId, MatchTimelineDto timeline)
    {
        var result = new List<JungleFirstClear>();
        if (timeline.Frames.Count == 0)
        {
            return result;
        }

        // Frames are ascending by TimestampMs (Riot guarantee); restrict to the
        // first-clear window.
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
            if (clear.Samples.Count > 0)
            {
                result.Add(clear);
            }
        }

        return result;
    }

    // Identify junglers from the frames alone: a participant whose jungle CS grows
    // by at least MinJungleCsForJungler over the window. Riot's per-team single
    // jungler role isn't needed — anyone who actually clears camps qualifies.
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

    private static JungleFirstClear BuildClear(
        string matchId,
        int participantId,
        List<MatchTimelineFrameDto> frames)
    {
        var samples = new List<JungleClearSample>();
        string? startCamp = null;
        int? fullClearTimeMs = null;

        foreach (var frame in frames)
        {
            var participantFrame = frame.ParticipantFrames
                .FirstOrDefault(pf => pf.ParticipantId == participantId);
            if (participantFrame is null || participantFrame.X is not { } x || participantFrame.Y is not { } y)
            {
                continue;
            }

            var jungleCs = participantFrame.JungleMinionsKilled;

            samples.Add(new JungleClearSample
            {
                TimestampMs = frame.TimestampMs,
                JungleCs = jungleCs,
                X = x,
                Y = y,
            });

            // While jungle CS is still 0 the jungler has not opened a camp yet, so
            // wherever he stands is the camp he is about to take. Later such frames
            // overwrite earlier ones: the t=0 frame catches him still in fountain,
            // the 1:00 frame catches him waiting on the camp.
            if (jungleCs == 0)
            {
                var camp = JungleCamps.NearestCamp(x, y);
                if (camp != JungleCamp.Unknown && JungleCamps.IsFirstClearCamp(camp))
                {
                    startCamp = camp.ToString();
                }
            }

            if (fullClearTimeMs is null && jungleCs >= JungleCamps.FullClearJungleCs)
            {
                fullClearTimeMs = frame.TimestampMs;
            }
        }

        return new JungleFirstClear
        {
            MatchId = matchId,
            ParticipantId = participantId,
            StartCamp = startCamp,
            Samples = samples,
            FullClearTimeMs = fullClearTimeMs,
        };
    }
}
