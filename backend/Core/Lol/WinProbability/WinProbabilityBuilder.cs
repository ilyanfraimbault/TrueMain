namespace Core.Lol.WinProbability;

/// <summary>
/// A finished game's win-probability curve and the moments that swung it (#1911), from its
/// timeline and <see cref="WinProbabilityModel"/>. A C# port of <c>buildWinProbability</c> in
/// <c>web/layers/common/app/utils/win-probability-timeline.ts</c>, which the desktop runs on
/// the client's own timeline; <c>web/shared/fixtures/win-probability-timeline.json</c> keeps
/// the two identical (<c>WinProbabilityParityTests</c>) — change one, change both.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The curve reads team 100's chance at every frame of the timeline (one a minute) and at the end of the game.</item>
/// <item>A turning point is a kill, a turret, an inhibitor, a drake, the Elder or the Baron, weighed at its own
/// time: the chance just after it minus just before, only that event changing. Creep score and level are
/// interpolated between the frames around it, and kills and objectives are steps, so that delta is exact under
/// the model.</item>
/// <item>The other epic monsters (Rift Herald, Voidgrubs, Atakhan) are listed as objectives with no delta: the
/// model does not weigh them.</item>
/// <item>No curve for a game under fifteen minutes (a remake or a surrender before the lanes mean anything), nor
/// without the five lanes paired across sides.</item>
/// </list>
/// </remarks>
public static class WinProbabilityBuilder
{
    /// <summary>Below this, no curve.</summary>
    public const int MinDurationMs = 15 * 60_000;

    /// <summary>How many turning points are kept, largest first.</summary>
    public const int SwingLimit = 10;

    private const int Blue = 100;
    private const int Red = 200;

    /// <summary>The game's curve and turning points, read for team 100; null when the game gets none (see above).</summary>
    public static WinProbabilityCurve? Build(WinProbabilityTimeline timeline)
    {
        if (timeline.DurationMs < MinDurationMs || timeline.Frames.Count == 0)
        {
            return null;
        }

        var pairs = LanePairs(timeline);
        if (pairs is null)
        {
            return null;
        }

        var teams = new Dictionary<int, int>();
        foreach (var participant in timeline.Participants)
        {
            teams[participant.ParticipantId] = participant.TeamId;
        }

        // Stable sorts: frames and events at the same millisecond keep the timeline's order.
        var frames = timeline.Frames.OrderBy(frame => frame.Ms).ToList();
        var events = timeline.Events
            .Where(e => e.Type is "CHAMPION_KILL" or "BUILDING_KILL" or "ELITE_MONSTER_KILL")
            .OrderBy(e => e.Ms)
            .ToList();

        var times = frames.Select(frame => frame.Ms).Where(ms => ms <= timeline.DurationMs).ToList();
        if (times.Count == 0 || times[^1] != timeline.DurationMs)
        {
            times.Add(timeline.DurationMs);
        }

        var curve = new GameState(frames, pairs, teams);
        var next = 0;
        var points = new List<WinProbabilityPoint>(times.Count);
        foreach (var ms in times)
        {
            while (next < events.Count && events[next].Ms <= ms)
            {
                curve.Apply(events[next++]);
            }

            points.Add(new WinProbabilityPoint(ms, curve.Probability(ms)));
        }

        var state = new GameState(frames, pairs, teams);
        var swings = new List<WinProbabilitySwing>();
        var objectives = new List<WinProbabilityObjective>();
        foreach (var e in events)
        {
            var kind = KindOf(e);
            var before = state.Probability(e.Ms);
            state.Apply(e);
            var delta = state.Probability(e.Ms) - before;
            var sideId = e.Type == "CHAMPION_KILL" ? TeamOf(teams, e.KillerId ?? 0) : SideOf(e, teams);

            if (e.Type == "ELITE_MONSTER_KILL" && sideId is { } monsterSide)
            {
                objectives.Add(new WinProbabilityObjective(
                    e.Ms,
                    e.MonsterType ?? string.Empty,
                    NullIfEmpty(e.MonsterSubType),
                    monsterSide,
                    kind is null ? null : delta));
            }

            if (kind is null || sideId is not { } side || delta == 0)
            {
                continue;
            }

            var isKill = kind == WinProbabilitySwingKinds.Kill;
            swings.Add(new WinProbabilitySwing(
                e.Ms,
                kind,
                side,
                delta,
                isKill ? e.KillerId ?? 0 : 0,
                isKill ? e.VictimId ?? 0 : 0,
                isKill ? e.AssistIds?.Count ?? 0 : 0,
                isKill ? e.Bounty : null,
                e.Type == "BUILDING_KILL" ? NullIfEmpty(e.LaneType) : null,
                kind == WinProbabilitySwingKinds.Turret ? NullIfEmpty(e.TowerType) : null,
                kind == WinProbabilitySwingKinds.Dragon ? NullIfEmpty(e.MonsterSubType) : null));
        }

        var top = swings
            .OrderByDescending(swing => Math.Abs(swing.Delta))
            .ThenBy(swing => swing.Ms)
            .Take(SwingLimit)
            .ToList();
        return new WinProbabilityCurve(points, top, objectives);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static int? TeamOf(Dictionary<int, int> teams, int participantId)
        => teams.TryGetValue(participantId, out var teamId) ? teamId : null;

    /// <summary>The five lanes, each a blue and a red player — or null if any lane lacks exactly one of each.</summary>
    private static List<LanePair>? LanePairs(WinProbabilityTimeline timeline)
    {
        var pairs = new List<LanePair>(WinProbabilityModel.Lanes.Count);
        foreach (var lane in WinProbabilityModel.Lanes)
        {
            var blue = timeline.Participants.Where(p => p.TeamId == Blue && p.Position == lane).ToList();
            var red = timeline.Participants.Where(p => p.TeamId == Red && p.Position == lane).ToList();
            if (blue.Count != 1 || red.Count != 1)
            {
                return null;
            }

            pairs.Add(new LanePair(lane, blue[0].ParticipantId, red[0].ParticipantId));
        }

        return pairs;
    }

    /// <summary>The side an event is for, or null when the timeline does not say.</summary>
    private static int? SideOf(WinProbabilityTimelineEvent e, Dictionary<int, int> teams)
    {
        if (e.Type == "BUILDING_KILL")
        {
            // The building's own side lost it.
            return e.TeamId switch
            {
                Blue => Red,
                Red => Blue,
                _ => null,
            };
        }

        if (e.Type == "ELITE_MONSTER_KILL" && e.KillerTeamId is Blue or Red)
        {
            return e.KillerTeamId;
        }

        return TeamOf(teams, e.KillerId ?? 0);
    }

    /// <summary>The turning-point kind of an event, or null when the model does not weigh it.</summary>
    private static string? KindOf(WinProbabilityTimelineEvent e)
        => e.Type switch
        {
            "CHAMPION_KILL" => (e.KillerId ?? 0) > 0 ? WinProbabilitySwingKinds.Kill : null,
            "BUILDING_KILL" => e.BuildingType switch
            {
                "TOWER_BUILDING" => WinProbabilitySwingKinds.Turret,
                "INHIBITOR_BUILDING" => WinProbabilitySwingKinds.Inhibitor,
                _ => null,
            },
            "ELITE_MONSTER_KILL" => e.MonsterType switch
            {
                "DRAGON" => e.MonsterSubType == "ELDER_DRAGON" ? WinProbabilitySwingKinds.Elder : WinProbabilitySwingKinds.Dragon,
                "BARON_NASHOR" => WinProbabilitySwingKinds.Baron,
                _ => null,
            },
            _ => null,
        };

    private sealed record LanePair(string Lane, int Blue, int Red);

    private sealed class MapState
    {
        public int Turrets { get; set; }

        /// <summary>The game time (ms) each enemy inhibitor this side destroyed stands again.</summary>
        public List<int> Inhibitors { get; } = [];

        public int Dragons { get; set; }

        public int? BaronUntilMs { get; set; }

        public int? ElderUntilMs { get; set; }
    }

    private sealed class GameState(
        IReadOnlyList<WinProbabilityTimelineFrame> frames,
        IReadOnlyList<LanePair> pairs,
        Dictionary<int, int> teams)
    {
        private readonly Dictionary<int, int> _kills = [];
        private readonly MapState _blue = new();
        private readonly MapState _red = new();

        public void Apply(WinProbabilityTimelineEvent e)
        {
            var kind = KindOf(e);
            if (kind == WinProbabilitySwingKinds.Kill)
            {
                var killer = e.KillerId!.Value;
                _kills[killer] = _kills.GetValueOrDefault(killer) + 1;
                return;
            }

            var sideId = SideOf(e, teams);
            if (kind is null || sideId is null)
            {
                return;
            }

            var side = sideId == Blue ? _blue : _red;
            switch (kind)
            {
                case WinProbabilitySwingKinds.Turret:
                    side.Turrets++;
                    break;
                case WinProbabilitySwingKinds.Inhibitor:
                    side.Inhibitors.Add(e.Ms + (WinProbabilityModel.InhibitorRespawnSeconds * 1000));
                    break;
                case WinProbabilitySwingKinds.Dragon:
                    side.Dragons++;
                    break;
                case WinProbabilitySwingKinds.Elder:
                    side.ElderUntilMs = e.Ms + (WinProbabilityModel.ElderBuffSeconds * 1000);
                    break;
                case WinProbabilitySwingKinds.Baron:
                    side.BaronUntilMs = e.Ms + (WinProbabilityModel.BaronBuffSeconds * 1000);
                    break;
            }
        }

        /// <summary>Team 100's chance at <paramref name="ms"/>, with the events applied so far.</summary>
        public double Probability(int ms)
        {
            var leads = new LaneLead[pairs.Count];
            for (var i = 0; i < pairs.Count; i++)
            {
                var (lane, blue, red) = pairs[i];
                leads[i] = new LaneLead(
                    lane,
                    FrameValue(blue, ms, cs: true) - FrameValue(red, ms, cs: true),
                    FrameValue(blue, ms, cs: false) - FrameValue(red, ms, cs: false),
                    _kills.GetValueOrDefault(blue) - _kills.GetValueOrDefault(red));
            }

            return WinProbabilityModel.WinProbability(leads, SideMapAt(_blue, ms), SideMapAt(_red, ms), ms / 1000.0);
        }

        private static SideMap SideMapAt(MapState side, int ms)
            => new(
                side.Turrets,
                side.Inhibitors.Count(respawn => ms < respawn),
                side.Dragons,
                side.BaronUntilMs is { } baronUntil && ms < baronUntil,
                side.ElderUntilMs is { } elderUntil && ms < elderUntil);

        /// <summary>A player's creep score or level at <paramref name="ms"/>: linear between the frames around it, the nearest outside them.</summary>
        private double FrameValue(int participantId, int ms, bool cs)
        {
            if (ms <= frames[0].Ms)
            {
                return Read(0, participantId, cs);
            }

            var last = frames.Count - 1;
            if (ms >= frames[last].Ms)
            {
                return Read(last, participantId, cs);
            }

            var upper = 0;
            while (frames[upper].Ms < ms)
            {
                upper++;
            }

            var from = frames[upper - 1];
            var to = frames[upper];
            var low = Read(upper - 1, participantId, cs);
            return low + ((Read(upper, participantId, cs) - low) * (double)(ms - from.Ms) / (to.Ms - from.Ms));
        }

        private double Read(int index, int participantId, bool cs)
        {
            foreach (var player in frames[index].Players)
            {
                if (player.ParticipantId == participantId)
                {
                    return cs ? player.Cs : player.Level;
                }
            }

            return 0;
        }
    }
}
