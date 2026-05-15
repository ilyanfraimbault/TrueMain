export interface StaticItemData {
  id: number
  name: string
  iconUrl: string
  totalGold: number
}

export interface StaticSummonerSpellData {
  id: number
  name: string
  iconUrl: string
}

export interface StaticChampionSpellData {
  key: 'Q' | 'W' | 'E'
  name: string
  iconUrl: string
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

export interface ChampionStaticData {
  championName: string | null
  championIconUrl: string | null
  items: Record<number, StaticItemData>
  summonerSpells: Record<number, StaticSummonerSpellData>
  championSpells: Record<string, StaticChampionSpellData>
  perks: Record<number, StaticPerkData>
  perkStyles: Record<number, StaticPerkStyleData>
}

export interface ChampionStaticListItem {
  championId: number
  name: string
  iconUrl: string
}
