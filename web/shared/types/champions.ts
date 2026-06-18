export interface ChampionSummaryResponse {
  championId: number
  games: number
  wins: number
  winRate: number
  pickRate: number
  lanePlayRate: number
  trueMainCount: number
  /** OPGG-style performance tier: 'S' | 'A' | 'B' | 'C' | 'D' (patch-relative). */
  tier: string
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

export interface ChampionTrendResponse {
  championId: number
  position: string
  points: ChampionTrendPoint[]
}

export interface ChampionTrendPoint {
  patch: string
  winRate: number
  pickRate: number
  games: number
}

/**
 * A champion's average lead vs its lane opponent at each minute mark
 * (5/10/15/20/30), computed live from per-interval timeline snapshots. Positive
 * diffs mean the champion is ahead of the opposing lane at that interval.
 */
export interface ChampionTimelineLeadsResponse {
  championId: number
  position: string
  patch: string | null
  intervals: ChampionTimelineLeadsInterval[]
}

export interface ChampionTimelineLeadsInterval {
  intervalMinute: number
  games: number
  goldDiff: number
  csDiff: number
  killsDiff: number
  levelDiff: number
  xpDiff: number
  damageDiff: number
}

/** One lane-matchup row: the champion's record against a single opponent. */
export interface ChampionMatchupEntry {
  opponentChampionId: number
  games: number
  wins: number
  winRate: number
}

/**
 * All of a champion's lane matchups at a position, computed live from match
 * participants. The client slices a best/worst leaderboard out of it and
 * filters it for the opponent search.
 */
export interface ChampionMatchups {
  championId: number
  position: string
  patch: string | null
  matchups: ChampionMatchupEntry[]
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
