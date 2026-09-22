using TrueMain.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions.Composition;

/// <summary>
/// How loudly one selected game votes in the build aggregation (#1659).
///
/// <para>
/// Three independent factors, multiplied: how closely the game reproduces the requested
/// draft, whether it was played on the patch being recommended for, and whether a main of
/// the champion piloted it. Multiplicative rather than tiered, so no factor can silence
/// another — a main's game on the previous patch still outvotes a stranger's on the
/// current one, which is the intended reading of "the current patch counts more" without
/// turning it into "nothing else counts". The aggregator applies the win weight on top.
/// </para>
///
/// <para>
/// Pure and separate from <see cref="CompositionBuildQueryService"/> so the weights can be
/// tuned against a table of cases rather than against a database.
/// </para>
/// </summary>
public static class CompositionVoteWeight
{
    /// <summary>
    /// Vote weight of <paramref name="match"/> under <paramref name="options"/>.
    /// </summary>
    /// <param name="match">The selected game, carrying its similarity score, patch and pilot.</param>
    /// <param name="maxPossibleScore">
    /// Score a game reproducing every requested slot would reach. Zero when the request
    /// carried no slot at all, which leaves similarity out of the product entirely —
    /// every game then votes on patch and pilot alone.
    /// </param>
    /// <param name="options">Configured multipliers.</param>
    public static double For(
        CompositionMatchRef match,
        int maxPossibleScore,
        CompositionSearchOptions options)
    {
        var similarity = maxPossibleScore <= 0
            ? 1d
            : 1d + options.SimilarityWeightBoost * match.Score / maxPossibleScore;

        var patch = match.IsCurrentPatch
            ? options.CurrentPatchWeight
            : options.PreviousPatchWeight;

        var pilot = match.IsTruemain
            ? options.MainGameWeight
            : options.NonMainGameWeight;

        return similarity * patch * pilot;
    }
}
