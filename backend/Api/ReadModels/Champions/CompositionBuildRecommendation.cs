namespace TrueMain.ReadModels.Champions;

/// <summary>
/// Build recommendation aggregated from the top-K most similar games of the
/// composition recommender (#563), win-weighted. Reuses the champion-page
/// build vocabulary (<see cref="BuildRunePageReadModel"/> and friends) so the
/// frontend renders both from the same components. Every dimension is
/// nullable: a sparse top-K (missing timeline data, no rune selections) drops
/// the dimension instead of fabricating one.
/// </summary>
public sealed record CompositionBuildRecommendation
{
    /// <summary>Games the aggregation ran over (the top-K size).</summary>
    public int GamesConsidered { get; init; }

    public int Wins { get; init; }

    public BuildRunePageReadModel? RunePage { get; init; }

    public BuildItemSetReadModel? StarterItems { get; init; }

    /// <summary>Single-element item set, mirroring the champion-page shape.</summary>
    public BuildItemSetReadModel? Boots { get; init; }

    /// <summary>
    /// Most supported opening of the completed-items build order (up to the
    /// first three legendaries, in completion order).
    /// </summary>
    public BuildItemPathReadModel? CorePath { get; init; }

    /// <summary>
    /// Completed items outside <see cref="CorePath"/>, one single-element set
    /// per item, strongest support first.
    /// </summary>
    public IReadOnlyList<BuildItemSetReadModel> SituationalItems { get; init; } = [];

    public BuildSummonerSpellsReadModel? SummonerSpells { get; init; }

    public BuildSkillOrderReadModel? SkillOrder { get; init; }
}
