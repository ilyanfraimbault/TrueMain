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

export interface ChampionStaticData {
  championName: string | null
  championIconUrl: string | null
  items: Record<number, StaticItemData>
  summonerSpells: Record<number, StaticSummonerSpellData>
  championSpells: Record<string, StaticChampionSpellData>
}
