namespace Data.Entities;

/// <summary>
/// Transport DTO carried between <c>ChampionPatternProjector</c> (which
/// builds it from <see cref="ChampionAggregatePattern"/> + <see cref="ChampionDimRunePage"/>
/// + <see cref="ChampionDimBuild"/>) and the per-dimension aggregators in
/// the API. Not an EF entity — it has no backing table and exists only as
/// the aggregators' input shape.
/// </summary>
public class ChampionAggregateRunePage
{
    /// <summary>
    /// The first completed build item this rune page was correlated with
    /// (i.e. <see cref="ChampionAggregateBuild.BuildItem0"/> of the same
    /// pattern). Hydrated by the projector from <see cref="ChampionDimBuild.BuildItem0"/>
    /// of the same pattern row. 0 means "no build correlation" — kept for
    /// the build tree's per-root rune pick.
    /// </summary>
    public int FirstItemId { get; set; }

    public int PrimaryStyleId { get; set; }
    public int PrimaryKeystoneId { get; set; }
    public int PrimaryPerk1Id { get; set; }
    public int PrimaryPerk2Id { get; set; }
    public int PrimaryPerk3Id { get; set; }

    public int SecondaryStyleId { get; set; }
    public int SecondaryPerk1Id { get; set; }
    public int SecondaryPerk2Id { get; set; }

    public int StatOffense { get; set; }
    public int StatFlex { get; set; }
    public int StatDefense { get; set; }

    public int Games { get; set; }
    public int Wins { get; set; }
}
