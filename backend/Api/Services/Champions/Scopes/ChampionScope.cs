using Core.Lol.Map;

namespace TrueMain.Services.Champions.Scopes;

/// <summary>
/// The slice a champion read answers: one champion, at a position, on a patch, in an elo
/// band, over a population. Parsed once at the HTTP boundary (<c>Http/ChampionScopeParameter</c>)
/// and handed to the services whole, so every read spells "which games" the same way.
/// </summary>
/// <remarks>
/// Canonical by construction: whatever spelling a caller passes, the scope holds Riot's
/// upper-case lane, the <c>major.minor</c> patch and the canonical bracket, so two spellings of
/// one slice share a cache entry and a service never re-normalises. A position or bracket that
/// names nothing throws rather than widening to "every lane" / "every band" — answering a
/// lane-labelled question with every lane's games is a fabricated number, not a lenient one.
/// </remarks>
public sealed record ChampionScope
{
    /// <summary>Builds the canonical scope, or throws on a position or bracket that names nothing.</summary>
    /// <param name="championId">Riot champion id.</param>
    /// <param name="position">
    /// A lane (<c>TOP</c> … <c>UTILITY</c>, <c>MID</c>/<c>BOT</c> accepted);
    /// <see cref="LanePositions.AllPositions"/> for every position summed; or blank when the
    /// read resolves the position itself (the champion's, or the player's, dominant lane).
    /// The route's parse rule decides which of these a blank query value becomes, so a
    /// service never guesses what an absent one meant.
    /// </param>
    /// <param name="patch">A Riot patch in any form, or blank (or unparseable) for the read's default.</param>
    /// <param name="eloBracket">An elo bracket filter, or blank for every band.</param>
    /// <param name="population">Whose games are folded.</param>
    public ChampionScope(
        int championId,
        string? position,
        string? patch,
        string? eloBracket = null,
        ChampionPopulation population = ChampionPopulation.Truemains)
    {
        ChampionId = championId;
        Position = CanonicalPosition(position);
        Patch = PatchFilter.Normalize(patch);
        EloBracket = Core.Lol.Ranking.EloBracket.TryResolveFilter(eloBracket, out _)
            ? Core.Lol.Ranking.EloBracket.Normalize(eloBracket)
            : throw new ArgumentException($"'{eloBracket}' is not an elo bracket.", nameof(eloBracket));
        Population = population;
    }

    public int ChampionId { get; }

    /// <summary>
    /// A canonical lane, <see cref="LanePositions.AllPositions"/>, or <see langword="null"/>
    /// when the read resolves the position itself.
    /// </summary>
    public string? Position { get; }

    /// <summary>Canonical <c>major.minor</c> patch, or <see langword="null"/> for the read's default.</summary>
    public string? Patch { get; }

    /// <summary>Canonical elo bracket, or <see langword="null"/> for every band.</summary>
    public string? EloBracket { get; }

    public ChampionPopulation Population { get; }

    /// <summary>The one lane the slice is narrowed to, or <see langword="null"/> when it is not.</summary>
    public string? Lane => LanePositions.IsLane(Position) ? Position : null;

    /// <summary>
    /// The position of a read that cannot run without one. Its routes parse with a rule that
    /// rejects a blank value, so a null here is a wiring mistake, not a client error.
    /// </summary>
    public string RequiredPosition
        => Position ?? throw new InvalidOperationException(
            $"Champion {ChampionId}: this read needs a position, and its route accepted a scope without one.");

    /// <summary>
    /// The scope's contribution to a champion-read cache key. Spelled out rather than left to
    /// <c>ToString()</c> so adding a field cannot silently widen what the read returns while
    /// leaving the key — and the cached answer — unchanged.
    /// </summary>
    public string CacheToken()
        => $"{ChampionId}:{Position ?? "auto"}:{Patch ?? "auto"}"
            + $":{Core.Lol.Ranking.EloBracket.ResolveToken(EloBracket)}:{Population}";

    private static string? CanonicalPosition(string? position)
    {
        if (string.IsNullOrWhiteSpace(position))
        {
            return null;
        }

        if (string.Equals(position.Trim(), LanePositions.AllPositions, StringComparison.OrdinalIgnoreCase))
        {
            return LanePositions.AllPositions;
        }

        return LanePositions.Normalize(position)
            ?? throw new ArgumentException($"'{position}' is not a position.", nameof(position));
    }
}

/// <summary>Whose games a champion read folds.</summary>
public enum ChampionPopulation
{
    /// <summary>Accounts that are mains of the champion — the site's truemain population.</summary>
    Truemains,

    /// <summary>Every tracked player's games on the champion (#1346).</summary>
    Everyone,
}
