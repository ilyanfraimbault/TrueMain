using Core.Lol.Ranking;

namespace Ingestor.Processes.Components.LadderSync;

/// <summary>One (tier, division) slot of the paginated ladder sweep.</summary>
public sealed record LadderSweepSlot(string Tier, string Division);

/// <summary>
/// The ordered slot list the paginated sweep walks, and the cursor arithmetic over it (#1312).
/// </summary>
/// <remarks>
/// Kept apart from the process so the ordering and wrap-around are unit-testable without a Riot
/// client: an off-by-one here does not fail loudly, it silently skips a division on every sweep.
/// </remarks>
public static class LadderSweepPlan
{
    /// <summary>Riot's divisions within a tier, highest first.</summary>
    public static readonly IReadOnlyList<string> Divisions = ["I", "II", "III", "IV"];

    /// <summary>
    /// Tiers that have their own whole-ladder endpoint and therefore no divisions to page
    /// through. They are refreshed every run outside the request budget.
    /// </summary>
    public static readonly IReadOnlySet<string> ApexTiers =
        new HashSet<string>(StringComparer.Ordinal) { EloBracket.Master, EloBracket.Grandmaster, EloBracket.Challenger };

    /// <summary>
    /// Expands a configured tier scope into the slots to sweep, highest tier first and division
    /// I first within each tier. Apex and unknown tiers are dropped: the former are fetched
    /// whole, the latter cannot be paged.
    /// </summary>
    public static IReadOnlyList<LadderSweepSlot> BuildSlots(IEnumerable<string> tierScope)
    {
        var scope = Normalize(tierScope);

        return EloBracket.Ladder
            .Reverse()
            .Where(tier => !ApexTiers.Contains(tier) && scope.Contains(tier))
            .SelectMany(tier => Divisions.Select(division => new LadderSweepSlot(tier, division)))
            .ToList();
    }

    /// <summary>
    /// The apex tiers in the configured scope, highest first.
    /// </summary>
    public static IReadOnlyList<string> ApexTiersInScope(IEnumerable<string> tierScope)
    {
        var scope = Normalize(tierScope);

        return EloBracket.Ladder
            .Reverse()
            .Where(tier => ApexTiers.Contains(tier) && scope.Contains(tier))
            .ToList();
    }

    /// <summary>
    /// Index of <paramref name="slot"/> in <paramref name="slots"/>, or 0 when it is absent —
    /// which is what a cursor left behind by a previous, wider tier scope looks like. Restarting
    /// such a cursor at the top of the current scope is the only safe reading: the alternative,
    /// treating it as an offset, would land on an arbitrary division.
    /// </summary>
    public static int IndexOfOrStart(IReadOnlyList<LadderSweepSlot> slots, LadderSweepSlot? slot)
    {
        if (slot is null)
        {
            return 0;
        }

        for (var i = 0; i < slots.Count; i++)
        {
            if (string.Equals(slots[i].Tier, slot.Tier, StringComparison.Ordinal)
                && string.Equals(slots[i].Division, slot.Division, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return 0;
    }

    private static HashSet<string> Normalize(IEnumerable<string> tierScope)
    {
        return tierScope
            .Where(tier => !string.IsNullOrWhiteSpace(tier))
            .Select(tier => NormalizeTier(tier.Trim()))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Canonicalises a configured tier name, accepting the <c>GM</c> shorthand that
    /// <c>Discovery:TierScope</c> already allows.
    /// </summary>
    private static string NormalizeTier(string tier)
    {
        var upper = tier.ToUpperInvariant();
        return upper == "GM" ? EloBracket.Grandmaster : upper;
    }
}
