using Core.Lol.Performance;

namespace TrueMain.Services.Truemains.PlayerChampions;

/// <summary>One participant's timeline snapshot at a canonical minute mark.</summary>
/// <param name="ParticipantId">The participant the snapshot belongs to.</param>
/// <param name="Minute">Canonical mark (5, 10, 15, 20, 30).</param>
/// <param name="Cs">Lane minions + neutral monsters at that mark.</param>
/// <param name="Gold">Total gold at that mark.</param>
/// <param name="Xp">Experience at that mark.</param>
public readonly record struct TimelineMark(int ParticipantId, int Minute, int Cs, int Gold, int Xp);

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
/// Turns the raw timeline rows the read paths already load into the
/// timeline-derived input of <see cref="PerformanceScore"/>: the per-mark leads
/// over the lane opponent.
///
/// <para>Shared by every surface that scores a match (single-match detail, the
/// match-history feed, the player-scoped champion page) so all three grade the
/// same game identically — a row that says MVP and a detail panel that disagrees
/// is the bug this file exists to make unrepresentable.</para>
/// </summary>
public static class PerformanceInputs
{
    /// <summary>Shared empty mark set for a match with no timeline coverage.</summary>
    public static readonly IReadOnlyDictionary<(int ParticipantId, int Minute), TimelineMark> NoMarks
        = new Dictionary<(int ParticipantId, int Minute), TimelineMark>();

    /// <summary>
    /// Builds the scoring input of every participant of one match: side totals
    /// for the share components and the lane opponent's timeline marks for the
    /// lead components. One place, so the match feed, the detail page and the
    /// player-scoped champion panel cannot drift into grading the same game
    /// differently.
    /// </summary>
    /// <param name="participants">Every participant of the match.</param>
    /// <param name="durationSeconds">Game length in seconds; 0 disables the per-minute components.</param>
    /// <param name="marks">The match's timeline marks, keyed by (participant, minute).</param>
    public static IReadOnlyList<(ScoredParticipant Participant, PerformanceScoreInput Input)> BuildMatchInputs(
        IReadOnlyList<ScoredParticipant> participants,
        int durationSeconds,
        IReadOnlyDictionary<(int ParticipantId, int Minute), TimelineMark> marks)
    {
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(marks);

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

        var built = new List<(ScoredParticipant, PerformanceScoreInput)>(participants.Count);
        foreach (var p in participants)
        {
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
                LaneLeads = BuildLaneLeads(p.ParticipantId, FindLaneOpponent(participants, p), marks),
            }));
        }

        return built;
    }

    /// <summary>
    /// The lane opponent of <paramref name="self"/>: the participant on the
    /// other side sharing the same non-empty team position, <b>only when there
    /// is exactly one</b>. Null otherwise — an empty or unparsed position, a
    /// remake, or anomalous data with two enemies on one position.
    ///
    /// <para>The "exactly one" part is enforced rather than assumed: taking the
    /// first match would silently compare the player against an arbitrary row
    /// whose identity depends on Postgres' row order, which is exactly the kind
    /// of non-determinism the scorer must not have. No opponent means the lead
    /// components drop, which the model already handles.</para>
    /// </summary>
    /// <param name="participants">Every participant of the match.</param>
    /// <param name="self">The participant whose opponent is being resolved.</param>
    public static int? FindLaneOpponent(
        IReadOnlyList<ScoredParticipant> participants,
        ScoredParticipant self)
    {
        ArgumentNullException.ThrowIfNull(participants);

        if (string.IsNullOrEmpty(self.TeamPosition))
        {
            return null;
        }

        int? found = null;
        foreach (var other in participants)
        {
            if (other.TeamId == self.TeamId || other.TeamPosition != self.TeamPosition)
            {
                continue;
            }

            if (found is not null)
            {
                // A second candidate: the pairing is ambiguous, so there is no
                // lane opponent rather than a coin-flip one.
                return null;
            }

            found = other.ParticipantId;
        }

        return found;
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
    /// The canonical marks the ingestor stores, in order. Iterating this rather
    /// than the rows keeps the produced leads sorted and bounded even if the
    /// table ever holds an off-grid interval.
    /// </summary>
    private static ReadOnlySpan<int> CanonicalMinutes => [5, 10, 15, 20, 30];
}
