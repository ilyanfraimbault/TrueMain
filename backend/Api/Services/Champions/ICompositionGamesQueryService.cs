using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Hydrates a page of the composition selection into renderable game rows
/// (#940): the collapsed match row plus the pilot's Riot identity, for the
/// refs the similarity search picked.
/// </summary>
public interface ICompositionGamesQueryService
{
    Task<IReadOnlyList<CompositionGameReadModel>> HydrateAsync(
        IReadOnlyList<CompositionMatchRef> matches,
        CancellationToken ct);
}
