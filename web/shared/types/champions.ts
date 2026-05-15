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

export interface RunePageOptionResponse {
  /**
   * The first completed build item this rune page was correlated with.
   * 0 means "unknown" — backfilled rows from before the first
   * post-deploy aggregation cycle. Phase 6 (pattern junction) replaces
   * this partial correlation with full cross-dimension correlation; the
   * field is kept here only for backwards compatibility while that ships.
   */
  firstItemId: number
  primaryStyleId: number
  primaryKeystoneId: number
  primaryPerk1Id: number
  primaryPerk2Id: number
  primaryPerk3Id: number
  secondaryStyleId: number
  secondaryPerk1Id: number
  secondaryPerk2Id: number
  statOffense: number
  statFlex: number
  statDefense: number
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
  boots: ItemSetOptionResponse | null
  buildPath: BuildPathPreviewResponse | null
  summonerSpells: SummonerSpellOptionResponse | null
  skillOrder: SkillOrderOptionResponse | null
  runePage: RunePageOptionResponse | null
}

export interface ChampionAdvancedResponse {
  starterItemOptions: ItemSetOptionResponse[]
  summonerSpellOptions: SummonerSpellOptionResponse[]
  skillOrderOptions: SkillOrderOptionResponse[]
  runePageOptions: RunePageOptionResponse[]
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
  boots: ItemSetOptionResponse | null
  runePage: RunePageOptionResponse | null
  build: ChampionBuildTreeNodeResponse[]
}
