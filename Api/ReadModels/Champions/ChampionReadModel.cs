namespace TrueMain.ReadModels.Champions;

/// <summary>
/// Top-level read model returned by <c>GET /champions/{id}</c>. Composes the
/// summary, derived core, advanced breakdowns, and build tree for the
/// requested champion into the exact shape the API exposes.
/// </summary>
public sealed class ChampionReadModel
{
    public ChampionSummaryReadModel Summary { get; init; } = new();

    public ChampionCoreReadModel Core { get; init; } = new();

    public ChampionAdvancedDetailsReadModel Advanced { get; init; } = new();

    public ChampionBuildTreeReadModel BuildTree { get; init; } = new();
}
