namespace Data.Entities;

public class ChampionAggregateRunePage
{
    public Guid Id { get; set; }
    public Guid ScopeId { get; set; }
    public ChampionAggregateScope Scope { get; set; } = null!;

    /// <summary>
    /// The first completed build item this rune page was correlated with
    /// (i.e. <see cref="ChampionAggregateBuild.BuildItem0"/> of the same
    /// participant). 0 means "unknown" — backfilled rows use this until the
    /// next full aggregation run rebuilds the dimension with real values.
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
