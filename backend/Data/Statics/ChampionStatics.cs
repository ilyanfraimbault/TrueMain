namespace Data.Statics;

/// <summary>
/// The static attributes of a champion the profile fold reads from Data Dragon (#1449).
/// </summary>
/// <param name="ChampionId">Riot's numeric champion id (Data Dragon's <c>key</c>).</param>
/// <param name="Key">Data Dragon's string id (<c>"Aatrox"</c>, <c>"MonkeyKing"</c>).</param>
/// <param name="AttackRange">Base auto-attack range in game units.</param>
public sealed record ChampionStatics(int ChampionId, string Key, int AttackRange);
