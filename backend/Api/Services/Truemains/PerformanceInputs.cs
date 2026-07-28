using Core.Lol.Map;
using Core.Lol.Performance;

namespace TrueMain.Services.Truemains;

/// <summary>One participant's timeline snapshot at a canonical minute mark.</summary>
/// <param name="ParticipantId">The participant the snapshot belongs to.</param>
/// <param name="Minute">Canonical mark (5, 10, 15, 20, 30).</param>
/// <param name="Cs">Lane minions + neutral monsters at that mark.</param>
/// <param name="Gold">Total gold at that mark.</param>
/// <param name="Xp">Experience at that mark.</param>
public readonly record struct TimelineMark(int ParticipantId, int Minute, int Cs, int Gold, int Xp);

/// <summary>One early kill participation's map position.</summary>
/// <param name="ParticipantId">The participant who took part in the kill.</param>
/// <param name="X">Timeline x coordinate.</param>
/// <param name="Y">Timeline y coordinate.</param>
public readonly record struct KillSpot(int ParticipantId, int X, int Y);

/// <summary>
/// Turns the raw timeline rows the read paths already load into the two
/// timeline-derived inputs of <see cref="PerformanceScore"/>: the per-mark leads
/// over the lane opponent, and the early out-of-lane takedown count.
///
/// <para>Shared by every surface that scores a match (single-match detail, the
/// match-history feed, the player-scoped champion page) so all three grade the
/// same game identically — a row that says MVP and a detail panel that disagrees
/// is the bug this file exists to make unrepresentable.</para>
/// </summary>
public static class PerformanceInputs
{
    // Riot team id for the blue side (bottom-left of the map); 200 is red.
    private const int BlueTeamId = 100;

    /// <summary>
    /// Leads over the lane opponent at every canonical mark both sides have a
    /// snapshot for. Empty when there is no opponent (an unparsed team position,
    /// a remake) or when no mark is covered on both sides — which drops the
    /// laning and mid-game components rather than scoring them 0.
    /// </summary>
    public static IReadOnlyList<LaneLead> BuildLaneLeads(
        int participantId,
        int? opponentParticipantId,
        IReadOnlyDictionary<(int ParticipantId, int Minute), TimelineMark> marks)
    {
        ArgumentNullException.ThrowIfNull(marks);

        if (opponentParticipantId is not { } opponentId)
        {
            return Array.Empty<LaneLead>();
        }

        var leads = new List<LaneLead>();
        foreach (var minute in CanonicalMinutes)
        {
            if (!marks.TryGetValue((participantId, minute), out var self)
                || !marks.TryGetValue((opponentId, minute), out var foe))
            {
                continue;
            }

            leads.Add(new LaneLead(
                minute,
                self.Gold - foe.Gold,
                self.Cs - foe.Cs,
                self.Xp - foe.Xp));
        }

        return leads;
    }

    /// <summary>
    /// Counts the participant's early kill participations that happened outside
    /// their own lane, using the same <see cref="LolMap.IsRoam"/> classification
    /// the champion roam panel uses. The stored rows are already bounded to the
    /// early game by the ingestor, so no extra time window is applied here.
    ///
    /// <para>Returns <c>null</c> — "unknown", which drops the roam component —
    /// when the match has no kill-position coverage at all, or when the role has
    /// no own lane to leave (JUNGLE and any unparsed position). Returns <c>0</c>
    /// for a covered match in which the player never left their lane, which is a
    /// real result and is graded as one.</para>
    /// </summary>
    public static int? CountOutOfLaneTakedowns(
        int participantId,
        string? teamPosition,
        int teamId,
        bool matchHasKillPositions,
        IReadOnlyList<KillSpot> killSpots)
    {
        ArgumentNullException.ThrowIfNull(killSpots);

        if (!matchHasKillPositions)
        {
            return null;
        }

        var ownLane = OwnLane(teamPosition);
        if (ownLane is MapZone.Unknown)
        {
            return null;
        }

        var isBlueSide = teamId == BlueTeamId;
        var count = 0;
        foreach (var spot in killSpots)
        {
            if (spot.ParticipantId == participantId
                && LolMap.IsRoam(spot.X, spot.Y, ownLane, isBlueSide))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// The map lane a Riot team position calls home. JUNGLE and anything
    /// unrecognised map to <see cref="MapZone.Unknown"/>: a jungler has no own
    /// lane, so every gank would read as a roam.
    /// </summary>
    public static MapZone OwnLane(string? teamPosition) => teamPosition?.Trim().ToUpperInvariant() switch
    {
        "TOP" => MapZone.TopLane,
        "MIDDLE" => MapZone.MidLane,
        "BOTTOM" => MapZone.BotLane,
        "UTILITY" => MapZone.BotLane,
        _ => MapZone.Unknown,
    };

    /// <summary>
    /// The canonical marks the ingestor stores, in order. Iterating this rather
    /// than the rows keeps the produced leads sorted and bounded even if the
    /// table ever holds an off-grid interval.
    /// </summary>
    private static ReadOnlySpan<int> CanonicalMinutes => [5, 10, 15, 20, 30];
}
