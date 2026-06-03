export interface ChampionSummaryResponse {
  championId: number
  games: number
  wins: number
  winRate: number
  // Win rate on the previous patch for this (champion, lane); null when there
  // is no previous-patch data for the slice. Drives the list's WR delta arrow.
  winRatePrevious: number | null
  pickRate: number
  lanePlayRate: number
  trueMainCount: number
  position: string
  patchVersion: string
  lastUpdatedAtUtc: string
  topBuild: ChampionSummaryTopBuild | null
}

export interface ChampionSummaryTopBuild {
  firstItemId: number
  primaryKeystoneId: number
  secondaryStyleId: number
  itemPath: number[]
}

export interface ChampionResponse {
  championId: number
  patch: string
  position: string
  totalGames: number
  totalWins: number
  builds: ChampionBuild[]
}

export interface ChampionBuild {
  firstItemId: number
  primaryKeystoneId: number
  games: number
  pickRate: number
  winRate: number
  core: BuildCore
  variations: BuildVariations
  buildTree: BuildTreeNode[]
  runePages: BuildRunePage[]
}

export interface BuildCore {
  itemPath: BuildItemPath | null
  boots: BuildItemSet | null
  starterItems: BuildItemSet | null
  summonerSpells: BuildSummonerSpells | null
  skillOrder: BuildSkillOrder | null
  runePage: BuildRunePage | null
}

export interface BuildVariations {
  boots: BuildItemSet[]
  starterItems: BuildItemSet[]
  summonerSpells: BuildSummonerSpells[]
  skillOrder: BuildSkillOrder[]
}

export interface BuildTreeNode {
  itemId: number
  games: number
  wins: number
  pickRate: number
  children: BuildTreeNode[]
}

export interface BuildItemPath {
  itemIds: number[]
  games: number
  pickRate: number
  winRate: number
}

export interface BuildItemSet {
  itemIds: number[]
  games: number
  pickRate: number
  winRate: number
}

export interface BuildSummonerSpells {
  spell1Id: number
  spell2Id: number
  games: number
  pickRate: number
  winRate: number
}

export interface BuildSkillOrder {
  sequence: string[]
  games: number
  pickRate: number
  winRate: number
}

export interface BuildRunePage {
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
  pickRate: number
  winRate: number
}
