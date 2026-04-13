export interface ChampionSummaryResponse {
  championId: number
  games: number
  winRate: number
  trueMainCount: number
  position: string
  latestPatchVersion: string
  lastUpdatedAtUtc: string
}

export interface SummonerSpellOptionResponse {
  spell1Id: number
  spell2Id: number
  games: number
  playRate: number
  winRate: number
}

export interface SkillOrderOptionResponse {
  sequence: string[]
  games: number
  playRate: number
  winRate: number
}

export interface ItemSetOptionResponse {
  itemIds: number[]
  games: number
  playRate: number
  winRate: number
}

export interface BuildPathPreviewResponse {
  itemIds: number[]
}

export interface ChampionCoreResponse {
  sampleSize: number
  starterItems: ItemSetOptionResponse | null
  buildPath: BuildPathPreviewResponse | null
  summonerSpells: SummonerSpellOptionResponse | null
  skillOrder: SkillOrderOptionResponse | null
}

export interface ChampionAdvancedResponse {
  starterItemOptions: ItemSetOptionResponse[]
  summonerSpellOptions: SummonerSpellOptionResponse[]
  skillOrderOptions: SkillOrderOptionResponse[]
}

export interface ChampionResponse {
  summary: ChampionSummaryResponse
  core: ChampionCoreResponse
  advanced: ChampionAdvancedResponse
  buildTree: ChampionBuildTreeResponse
}

export interface ChampionBuildTreeNodeResponse {
  itemId: number
  games: number
  wins: number
  pickRate: number
  children: ChampionBuildTreeNodeResponse[]
}

export interface ChampionBuildTreeResponse {
  championId: number
  patch: string | null
  position: string | null
  riotAccountId: string | null
  platformId: string | null
  totalGames: number
  build: ChampionBuildTreeNodeResponse[]
}
