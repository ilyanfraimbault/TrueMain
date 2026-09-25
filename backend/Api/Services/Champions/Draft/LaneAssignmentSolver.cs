using Core.Lol.Map;

namespace TrueMain.Services.Champions.Draft;

/// <summary>
/// How often a champion is played in each lane, as a probability over the five
/// lanes. Sums to 1 when the champion was ever seen; an unseen champion carries
/// an empty map and the solver treats every lane as equally likely for it.
/// </summary>
public sealed record LanePrior
{
    public required int ChampionId { get; init; }

    /// <summary>P(lane | champion), keyed by Riot team position.</summary>
    public required IReadOnlyDictionary<string, double> ByLane { get; init; }

    /// <summary>
    /// Games the prior rests on, across every lane. Zero means we have never
    /// seen this champion and the prior is uniform — which the caller must be
    /// able to tell apart from a confident spread.
    /// </summary>
    public required int Games { get; init; }
}

/// <summary>One champion placed in one lane, with how sure we are.</summary>
public sealed record LaneAssignment
{
    public required int ChampionId { get; init; }
    public required string Position { get; init; }

    /// <summary>
    /// 0..1. Derived from how much worse the draft reads if this champion is
    /// moved elsewhere — not from the champion's own play rate, which says
    /// nothing about whether a *different* champion wanted the same lane.
    /// </summary>
    public required double Confidence { get; init; }

    /// <summary>True when the caller pinned this slot; then confidence is 1.</summary>
    public required bool Pinned { get; init; }
}

/// <summary>
/// Places the enemy champions into lanes.
///
/// <para>
/// This is an <em>assignment</em> problem, not five independent arg-maxes. Taking
/// each champion's most frequent lane on its own produces collisions — two
/// champions resolve to MIDDLE, nobody to JUNGLE — and our own lane opponent
/// then gets attributed to the wrong player, which is the one answer the draft
/// panel exists to give.
/// </para>
///
/// <para>
/// The solver enumerates every placement rather than running the Hungarian
/// algorithm. With five lanes there are at most 120 of them, so enumeration is
/// exact, costs nothing measurable, and is obviously correct on inspection —
/// where a hand-written Hungarian implementation is subtle and its bugs are
/// silent (a wrong-but-plausible lane, not a crash). Hungarian becomes worth it
/// only if the slot count ever grows, which for Summoner's Rift it will not.
/// </para>
/// </summary>
public static class LaneAssignmentSolver
{
    /// <summary>
    /// Floor on any single lane's probability. Without it a champion never seen
    /// in a lane contributes log(0) and the whole placement is discarded, so one
    /// off-meta pick would force every *other* champion into a worse lane. The
    /// floor keeps a surprising pick merely expensive instead of impossible.
    /// </summary>
    private const double MinimumLaneProbability = 1e-4;

    /// <summary>
    /// Two placements whose scores differ by less than this are treated as
    /// equally good, and the tie is broken towards what is already on screen.
    /// Picks land one at a time and the panel re-solves on each one; a panel
    /// that reshuffles while it is being read, with a timer running, is worse
    /// than one that is slightly wrong.
    /// </summary>
    private const double ScoreTieEpsilon = 1e-9;

    /// <summary>
    /// Place <paramref name="championIds"/> into lanes.
    /// </summary>
    /// <param name="championIds">
    /// The champions picked or hovered so far — fewer than five for most of the
    /// draft, which is the normal case and not a degraded one.
    /// </param>
    /// <param name="priors">Lane priors, by champion id. A champion with no entry gets a uniform prior.</param>
    /// <param name="pinned">
    /// Lanes the user corrected by hand, by champion id. Hard constraints: the
    /// solver places the rest around them, which is what lets one correction fix
    /// several wrong slots at once.
    /// </param>
    /// <param name="previous">
    /// The placement currently on screen, by champion id, used only to break
    /// ties. Null on the first solve.
    /// </param>
    public static IReadOnlyList<LaneAssignment> Solve(
        IReadOnlyList<int> championIds,
        IReadOnlyDictionary<int, LanePrior> priors,
        IReadOnlyDictionary<int, string>? pinned = null,
        IReadOnlyDictionary<int, string>? previous = null)
    {
        var champions = championIds.Distinct().ToList();
        if (champions.Count == 0)
        {
            return [];
        }

        pinned ??= new Dictionary<int, string>();

        var lanes = QueueDataQualityProfile.LanePositions;
        var placements = EnumeratePlacements(champions, lanes, pinned);
        if (placements.Count == 0)
        {
            // Two pins onto the same lane, or more champions than lanes. The
            // caller's pins are contradictory; drop them rather than return
            // nothing, since an unpinned answer is still useful.
            placements = EnumeratePlacements(champions, lanes, new Dictionary<int, string>());
            pinned = new Dictionary<int, string>();
        }

        if (placements.Count == 0)
        {
            // More champions than lanes: there is no placement to score, and
            // "none" is the honest answer rather than an exception.
            return [];
        }

        var scored = placements
            .Select(placement => (placement, score: Score(placement, priors)))
            .ToList();

        var bestScore = scored.Max(entry => entry.score);
        var best = scored
            .Where(entry => entry.score >= bestScore - ScoreTieEpsilon)
            .OrderByDescending(entry => AgreementWithPrevious(entry.placement, previous))
            .ThenByDescending(entry => entry.score)
            .First()
            .placement;

        return champions
            .Select(championId => new LaneAssignment
            {
                ChampionId = championId,
                Position = best[championId],
                Pinned = pinned.ContainsKey(championId),
                Confidence = pinned.ContainsKey(championId)
                    ? 1d
                    : ConfidenceFor(championId, best[championId], bestScore, scored),
            })
            .ToList();
    }

    /// <summary>
    /// Every way to put the champions in distinct lanes, honouring the pins.
    /// </summary>
    private static List<Dictionary<int, string>> EnumeratePlacements(
        IReadOnlyList<int> champions,
        IReadOnlyList<string> lanes,
        IReadOnlyDictionary<int, string> pinned)
    {
        var results = new List<Dictionary<int, string>>();
        var current = new Dictionary<int, string>();
        var taken = new HashSet<string>(StringComparer.Ordinal);

        // A pin onto a lane another pin already claimed makes the whole set
        // unsatisfiable; the caller handles the empty result.
        foreach (var (championId, lane) in pinned)
        {
            if (!champions.Contains(championId) || !taken.Add(lane))
            {
                return results;
            }
            current[championId] = lane;
        }

        var free = champions.Where(id => !pinned.ContainsKey(id)).ToList();
        Recurse(0);
        return results;

        void Recurse(int index)
        {
            if (index == free.Count)
            {
                results.Add(new Dictionary<int, string>(current));
                return;
            }

            foreach (var lane in lanes)
            {
                if (!taken.Add(lane))
                {
                    continue;
                }
                current[free[index]] = lane;
                Recurse(index + 1);
                current.Remove(free[index]);
                taken.Remove(lane);
            }
        }
    }

    /// <summary>
    /// Log-likelihood of a placement. Summed in log space so one implausible
    /// slot drags the whole placement down proportionally rather than being
    /// averaged away.
    /// </summary>
    private static double Score(
        IReadOnlyDictionary<int, string> placement,
        IReadOnlyDictionary<int, LanePrior> priors)
        => placement.Sum(slot => Math.Log(Probability(slot.Key, slot.Value, priors)));

    private static double Probability(
        int championId,
        string lane,
        IReadOnlyDictionary<int, LanePrior> priors)
    {
        if (!priors.TryGetValue(championId, out var prior) || prior.Games == 0)
        {
            // Never seen: every lane equally likely, so this champion adds a
            // constant and lets the others decide the placement.
            return 1d / QueueDataQualityProfile.LanePositions.Count;
        }

        return prior.ByLane.TryGetValue(lane, out var probability)
            ? Math.Max(probability, MinimumLaneProbability)
            : MinimumLaneProbability;
    }

    private static int AgreementWithPrevious(
        IReadOnlyDictionary<int, string> placement,
        IReadOnlyDictionary<int, string>? previous)
        => previous is null
            ? 0
            : placement.Count(slot =>
                previous.TryGetValue(slot.Key, out var lane) && lane == slot.Value);

    /// <summary>
    /// How much worse the best draft reads if this champion is moved off this
    /// lane, squashed onto 0..1.
    /// </summary>
    /// <remarks>
    /// A logistic on the log-likelihood margin: an even split between two
    /// readings lands at 0.5 — a coin flip, which is what it is — and a margin
    /// grows towards 1 without ever reaching it. Deliberately not the champion's
    /// own play rate: a champion played in one lane 95% of the time is still an
    /// uncertain call when another champion in the same draft wants that lane
    /// more.
    /// </remarks>
    private static double ConfidenceFor(
        int championId,
        string assignedLane,
        double bestScore,
        IReadOnlyList<(Dictionary<int, string> placement, double score)> scored)
    {
        var bestElsewhere = scored
            .Where(entry => entry.placement[championId] != assignedLane)
            .Select(entry => entry.score)
            .DefaultIfEmpty(double.NegativeInfinity)
            .Max();

        if (double.IsNegativeInfinity(bestElsewhere))
        {
            // Only one lane was ever available to it — a single champion, or
            // every other lane pinned away. Certain by construction.
            return 1d;
        }

        var margin = bestScore - bestElsewhere;
        return 1d / (1d + Math.Exp(-margin));
    }
}
