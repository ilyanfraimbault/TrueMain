export interface StaticItemData {
  id: number
  name: string
  iconUrl: string
  totalGold: number
  /** Terse one-liner from DDragon `item.plaintext`. Fallback when `description` is missing. */
  plaintext?: string
  /**
   * DDragon `item.description` — HTML fragment wrapped in `<mainText>` with
   * semantic tags (`<stats>`, `<attention>`, `<passive>`, `<physicalDamage>`,
   * `<scaleArmor>`, `<speed>`, ...). Parsed lazily on first hover by the
   * tooltip-parser; not preprocessed server-side.
   */
  description?: string
  /**
   * Whether the item can be bought directly in the shop (DDragon
   * `item.gold.purchasable`). Auto-granted transforms — support/role quest
   * upgrade stages, the empowered-recall boots upgrade — are `false`. Absent
   * defaults to purchasable. Used to keep such non-shop steps out of a
   * player's build order (they surface as `ITEM_PURCHASED` events even though
   * the player never bought them). See also `inStore`.
   */
  purchasable?: boolean
  /**
   * Whether the item is listed in the shop at all (DDragon `item.inStore`,
   * only ever emitted as `false`). Some quest transforms keep
   * `gold.purchasable = true` yet set `inStore = false`, so both flags are
   * checked when filtering non-shop build-order steps.
   */
  inStore?: boolean
}

export interface StaticSummonerSpellData {
  id: number
  name: string
  iconUrl: string
  /** Plain-text description from DDragon `summoner.description` (may contain `<br>`). */
  description?: string
  /** Display-ready cooldown in seconds (DDragon `summoner.cooldown[0]`). */
  cooldown?: number
  /** Minimum summoner level required to equip (DDragon `summoner.summonerLevel`). */
  summonerLevel?: number
}

export interface StaticChampionSpellData {
  key: 'Q' | 'W' | 'E' | 'R'
  name: string
  iconUrl: string
  /** Mostly clean description with occasional `<br>`, `<status>`, `<physicalDamage>` tags. */
  description?: string
  /** Cooldown string per rank (DDragon `spell.cooldownBurn`), e.g. "16/14/12/10/8". */
  cooldownBurn?: string
  /** Mana / energy cost string (DDragon `spell.costBurn`). */
  costBurn?: string
  /** "Mana" / "Energy" / "No Cost" (DDragon `spell.costType`). */
  costType?: string
  /** Range string (DDragon `spell.rangeBurn`). */
  rangeBurn?: string
}

/**
 * One rune perk row from Community Dragon's perks.json. Covers both the
 * keystone-tier perks (e.g. Press the Attack, Conqueror) and the minor
 * perks in primary / secondary trees, as well as the stat shards
 * (offense / flex / defense, ids 5001..5011).
 */
export interface StaticPerkData {
  id: number
  name: string
  iconUrl: string
  /** Single-line summary from CDragon `perk.shortDesc` (HTML with inline tags). */
  shortDesc?: string
  /** Full description from CDragon `perk.longDesc` (HTML with inline tags). */
  longDesc?: string
}

/**
 * One rune style ("tree") from Community Dragon's perkstyles.json
 * (e.g. 8000=Precision, 8100=Domination).
 */
export interface StaticPerkStyleData {
  id: number
  name: string
  iconUrl: string
}

/**
 * Per-champion static payload from `/api/static/[championId]`. Intentionally
 * narrow: items, summoner spells, perks and perk styles all come from the
 * patch-keyed endpoints (`/api/static/items`, `/api/static/summoner-spells`,
 * `/api/static/rune-tree`) so the detail page can dedupe those caches with the
 * list page instead of re-downloading them per champion.
 */
export interface ChampionStaticData {
  championName: string | null
  championIconUrl: string | null
  championSpells: Record<string, StaticChampionSpellData>
  /** Champion resource type ("Mana", "Energy", "Blood Well", ...). Used to
   *  resolve `{{ abilityresourcename }}` placeholders inside spell tooltips. */
  partype: string
}

export interface ChampionStaticListItem {
  championId: number
  name: string
  iconUrl: string
}

/**
 * One full primary rune tree: the keystone row (3–4 perks depending on the
 * style) followed by 3 sub-rows of 3 perks each. Secondary trees reuse the
 * same `subRows` but never show the `keystones` row.
 */
export interface RuneTreeStyle {
  styleId: number
  name: string
  iconUrl: string
  keystones: number[]
  subRows: number[][]
}

export interface RuneTreeResponse {
  styles: RuneTreeStyle[]
  perks: Record<number, StaticPerkData>
  perkStyles: Record<number, StaticPerkStyleData>
  /** 3 rows × 3 perks — fixed across all styles (offense / flex / defense). */
  shardSlots: number[][]
}
