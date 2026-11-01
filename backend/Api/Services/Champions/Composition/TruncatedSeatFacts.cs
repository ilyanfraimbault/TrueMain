using Data.Entities;

namespace TrueMain.Services.Champions.Composition;

/// <summary>
/// A truncated seat (#2368) as the build facts every live build read folds. The facts were
/// resolved at truncation by the same primitives <see cref="Builds.ParticipantBuildFactsLoader"/>
/// uses, so a game reads the same build whether it is still live or kept truncated.
/// </summary>
public static class TruncatedSeatFacts
{
    public static CompositionParticipantFacts From(TruncatedMatchSeat seat, double weight = 1d)
    {
        ArgumentNullException.ThrowIfNull(seat);

        return new CompositionParticipantFacts
        {
            Win = seat.Win,
            SimilarityWeight = weight,
            BuildItems = seat.BuildItemIds,
            BootsItemId = seat.BootsItemId,
            StarterItems = seat.StarterItemIds,
            Spell1Id = seat.Summoner1Id,
            Spell2Id = seat.Summoner2Id,
            SkillOrderKey = seat.SkillOrderKey,
            RunePage = RunePage(seat),
        };
    }

    /// <summary>The seat's rune page, or null when its perks were not complete at truncation.</summary>
    private static CompositionRunePageFacts? RunePage(TruncatedMatchSeat seat)
        => seat.RunePerkIds is not [var keystone, var perk1, var perk2, var perk3, var secondary1, var secondary2]
            ? null
            : new CompositionRunePageFacts(
                seat.PrimaryStyleId,
                keystone,
                perk1,
                perk2,
                perk3,
                seat.SubStyleId,
                secondary1,
                secondary2,
                seat.PerksOffense,
                seat.PerksFlex,
                seat.PerksDefense);
}
