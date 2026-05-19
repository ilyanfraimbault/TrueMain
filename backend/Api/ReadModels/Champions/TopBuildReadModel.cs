namespace TrueMain.ReadModels.Champions;

/// <summary>
/// Summary of the most-played build for a <c>(champion, position)</c> pair on
/// the active patch. Surfaces just enough for the directory list to render
/// the keystone, secondary tree, and item sequence inline — full build /
/// runes / variations remain on <c>GET /champions/{id}</c>.
/// </summary>
public sealed class TopBuildReadModel
{
    /// <summary>First completed item (<c>BuildItem0</c>) of the dominant
    /// <see cref="Data.Entities.ChampionDimBuild"/> for this slice.</summary>
    public int FirstItemId { get; init; }

    /// <summary>Primary keystone of the rune page tied to the dominant build.</summary>
    public int PrimaryKeystoneId { get; init; }

    /// <summary>Secondary tree (style) ID — frontend resolves the tree icon
    /// via the shared rune-tree static payload.</summary>
    public int SecondaryStyleId { get; init; }

    /// <summary>Raw item progression of the top <c>ChampionDimBuild</c>,
    /// <c>BuildItem0</c>..<c>BuildItem6</c> stripped of zero slots. The most
    /// common exact sequence rather than a probabilistic "consensus path" —
    /// cheap to compute over the directory and still meaningful at a glance.</summary>
    public IReadOnlyList<int> ItemPath { get; init; } = [];
}
