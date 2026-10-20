using Core.Lol.Map;
using TrueMain.Services.Champions.Draft;

namespace TrueMain.Services.Truemains.Live;

/// <summary>
/// The lanes of a live game. spectator-v5 gives none, so each team is placed by the same
/// solver the draft panel uses (#1906) — the champions' lane priors as an assignment, not
/// five independent guesses — with the one hard fact the game does give: the player who
/// took Smite is the jungler.
/// </summary>
public static class LiveLanes
{
    /// <summary>Summoner's Rift; every other map has no lanes to infer.</summary>
    public const int SummonersRiftMapId = 11;

    public const int SmiteSpellId = 11;

    /// <summary>
    /// The inferred position of each participant, keyed by its index in Riot's participant
    /// list. Empty outside a 5v5 Summoner's Rift game.
    /// </summary>
    public static async Task<Dictionary<int, string>> InferAsync(
        RiotActiveGameDto game,
        ILanePriorQueryService lanePriors,
        CancellationToken ct)
    {
        var teams = game.Participants
            .Select((participant, index) => (participant, index))
            .GroupBy(p => p.participant.TeamId)
            .ToList();

        // Two teams of five distinct champions: anything else (an arena, a custom with
        // duplicates) has no lane reading to make.
        if (game.MapId != SummonersRiftMapId
            || teams.Count != 2
            || teams.Any(t => t.Count() != LanePositions.All.Count
                              || t.Select(p => p.participant.ChampionId).Distinct().Count() != LanePositions.All.Count))
        {
            return [];
        }

        var priors = await lanePriors.GetAsync(
            game.Participants.Select(p => p.ChampionId).Distinct().ToList(),
            patch: null,
            ct);

        var positions = new Dictionary<int, string>();
        foreach (var team in teams)
        {
            var smiters = team
                .Where(p => p.participant.Spell1Id == SmiteSpellId || p.participant.Spell2Id == SmiteSpellId)
                .ToList();
            var pinned = smiters.Count == 1
                ? new Dictionary<int, string> { [smiters[0].participant.ChampionId] = "JUNGLE" }
                : null;

            var placement = LaneAssignmentSolver
                .Solve(team.Select(p => p.participant.ChampionId).ToList(), priors, pinned)
                .ToDictionary(a => a.ChampionId, a => a.Position);

            foreach (var (participant, index) in team)
            {
                if (placement.TryGetValue(participant.ChampionId, out var position))
                {
                    positions[index] = position;
                }
            }
        }

        return positions;
    }

    /// <summary>Sort key putting a team in lane order, unknown lanes last.</summary>
    public static int Order(string? position)
    {
        var index = position is null ? -1 : IndexOf(position);
        return index < 0 ? LanePositions.All.Count : index;
    }

    private static int IndexOf(string position)
    {
        for (var i = 0; i < LanePositions.All.Count; i++)
        {
            if (string.Equals(LanePositions.All[i], position, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
