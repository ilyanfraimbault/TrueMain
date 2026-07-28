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
/// The end-of-game half of one participant's scoring inputs, in the shape every
/// surface can project its own row type into.
/// </summary>
/// <param name="ParticipantId">Riot participant id within the match (1..10).</param>
/// <param name="TeamId">100 = blue side, 200 = red side.</param>
/// <param name="TeamPosition">Riot team position; null or empty when unassigned.</param>
/// <param name="Win">Whether this participant's side won.</param>
/// <param name="Kills">Champion kills.</param>
/// <param name="Deaths">Deaths.</param>
/// <param name="Assists">Assists.</param>
/// <param name="Cs">Lane minions + neutral monsters.</param>
/// <param name="DamageToChampions">Total damage dealt to champions.</param>
/// <param name="GoldEarned">Total gold earned.</param>
/// <param name="VisionScore">End-of-game vision score.</param>
public readonly record struct ScoredParticipant(
    int ParticipantId,
    int TeamId,
    string? TeamPosition,
    bool Win,
    int Kills,
    int Deaths,
    int Assists,
    int Cs,
    int DamageToChampions,
    int GoldEarned,
    int VisionScore);

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

    /// <summary>Shared empty mark set for a match with no timeline coverage.</summary>
    public static readonly IReadOnlyDictionary<(int ParticipantId, int Minute), TimelineMark> NoMarks
        = new Dictionary<(int ParticipantId, int Minute), TimelineMark>();

    /// <summary>
    /// Builds the scoring input of every participant of one match: side totals
    /// for the share components, the lane opponent's timeline marks for the lead
    /// components, and the match's early kill positions for the roam component.
    /// One place, so the match feed, the detail page and the player-scoped
    /// champion panel cannot drift into grading the same game differently.
    /// </summary>
    /// <param name="participants">Every participant of the match.</param>
    /// <param name="durationSeconds">Game length in seconds; 0 disables the per-minute components.</param>
    /// <param name="marks">The match's timeline marks, keyed by (participant, minute).</param>
    /// <param name="killSpots">The match's early kill participations; empty means no coverage.</param>
    public static IReadOnlyList<(ScoredParticipant Participant, PerformanceScoreInput Input)> BuildMatchInputs(
        IReadOnlyList<ScoredParticipant> participants,
        int durationSeconds,
        IReadOnlyDictionary<(int ParticipantId, int Minute), TimelineMark> marks,
        IReadOnlyList<KillSpot> killSpots)
    {
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(marks);
        ArgumentNullException.ThrowIfNull(killSpots);

        var teamKills = new Dictionary<int, int>();
        var teamDamage = new Dictionary<int, int>();
        var teamGold = new Dictionary<int, int>();
        foreach (var p in participants)
        {
            teamKills[p.TeamId] = teamKills.GetValueOrDefault(p.TeamId) + p.Kills;
            teamDamage[p.TeamId] = teamDamage.GetValueOrDefault(p.TeamId) + p.DamageToChampions;
            teamGold[p.TeamId] = teamGold.GetValueOrDefault(p.TeamId) + p.GoldEarned;
        }

        var durationMinutes = durationSeconds > 0 ? durationSeconds / 60d : 0d;
        var hasKillPositions = killSpots.Count > 0;

        var built = new List<(ScoredParticipant, PerformanceScoreInput)>(participants.Count);
        foreach (var p in participants)
        {
            // Lane opponent: the single participant on the other side sharing the
            // same non-empty team position. A position with anything other than
            // exactly one player per side simply gets none, which drops the lead
            // components rather than comparing against an arbitrary row.
            int? opponentId = null;
            if (!string.IsNullOrEmpty(p.TeamPosition))
            {
                foreach (var o in participants)
                {
                    if (o.TeamId != p.TeamId && o.TeamPosition == p.TeamPosition)
                    {
                        opponentId = o.ParticipantId;
                        break;
                    }
                }
            }

            built.Add((p, new PerformanceScoreInput
            {
                TeamPosition = p.TeamPosition ?? string.Empty,
                Kills = p.Kills,
                Deaths = p.Deaths,
                Assists = p.Assists,
                TeamKills = teamKills.GetValueOrDefault(p.TeamId),
                DamageToChampions = p.DamageToChampions,
                TeamDamageToChampions = teamDamage.GetValueOrDefault(p.TeamId),
                GoldEarned = p.GoldEarned,
                TeamGoldEarned = teamGold.GetValueOrDefault(p.TeamId),
                Cs = p.Cs,
                VisionScore = p.VisionScore,
                GameDurationMinutes = durationMinutes,
                LaneLeads = BuildLaneLeads(p.ParticipantId, opponentId, marks),
                OutOfLaneTakedowns = CountOutOfLaneTakedowns(
                    p.ParticipantId, p.TeamPosition, p.TeamId, hasKillPositions, killSpots),
            }));
        }

        return built;
    }

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
