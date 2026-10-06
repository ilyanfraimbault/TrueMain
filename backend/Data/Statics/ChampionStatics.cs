namespace Data.Statics;

/// <summary>
/// The static attributes of a champion read from Data Dragon: its attack range for the
/// profile fold (#1449), its attack and magic ratings for the damage-profile fallback (#1905).
/// </summary>
/// <param name="ChampionId">Riot's numeric champion id (Data Dragon's <c>key</c>).</param>
/// <param name="Key">Data Dragon's string id (<c>"Aatrox"</c>, <c>"MonkeyKing"</c>).</param>
/// <param name="AttackRange">Base auto-attack range in game units.</param>
/// <param name="AttackRating">
/// Data Dragon's hand-authored <c>info.attack</c> rating (0–10), or null when absent. Coarse
/// and old: only ever a classification of last resort, never a measured share.
/// </param>
/// <param name="MagicRating">Data Dragon's <c>info.magic</c> rating (0–10), or null when absent.</param>
public sealed record ChampionStatics(
    int ChampionId,
    string Key,
    int AttackRange,
    int? AttackRating = null,
    int? MagicRating = null);
