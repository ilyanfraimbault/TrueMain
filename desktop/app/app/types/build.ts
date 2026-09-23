/**
 * The slice of `ChampionResponse` (the site's champion page endpoint) the draft
 * reads. Mirrors `web/shared/types/champions.ts`; fields the app does not draw
 * are left out rather than copied.
 */
interface Measured {
  games: number
  pickRate: number
  winRate: number
}

export interface BuildItemSet extends Measured {
  itemIds: number[]
}

export interface BuildSummonerSpells extends Measured {
  spell1Id: number
  spell2Id: number
}

export interface BuildSkillOrder extends Measured {
  /** Max order, e.g. `['Q', 'W', 'E']`. */
  sequence: string[]
}

export interface BuildRunePage extends Measured {
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
}

/** The six blocks of the core view, whichever endpoint they came from. */
export interface BuildCoreView {
  itemPath: BuildItemSet | null
  boots: BuildItemSet | null
  starterItems: BuildItemSet | null
  summonerSpells: BuildSummonerSpells | null
  skillOrder: BuildSkillOrder | null
  runePage: BuildRunePage | null
}

/** One node of the item-progression tree: an item, and what was built after it. */
export interface BuildTreeNode {
  itemId: number
  games: number
  wins: number
  /** Share of the parent's games that went on to this item. */
  pickRate: number
  children: BuildTreeNode[]
}

export interface ChampionBuild {
  firstItemId: number
  buildTree: BuildTreeNode[]
  primaryKeystoneId: number
  games: number
  pickRate: number
  winRate: number
  core: BuildCoreView
}

export interface ChampionBuildResponse {
  championId: number
  patch: string | null
  position: string | null
  totalGames: number
  builds: ChampionBuild[]
}

/** One known pick of the draft, as `POST /champions/{id}/composition-build` takes it. */
export interface CompositionSlot {
  championId: number
  position: string
}

export interface CompositionBuildRequest {
  position: string
  allies: CompositionSlot[]
  enemies: CompositionSlot[]
}

/** The slice of `CompositionBuildResponse` the draft reads. */
export interface CompositionBuildResponse {
  championId: number
  position: string
  /** The draft pinned the lane opponent; the sample is then that matchup only. */
  matchupRequested: boolean
  /** False when that matchup was never recorded — the build is then empty. */
  matchupFound: boolean
  confidence: {
    sampleSize: number
    truemainGameCount: number
    /** 0..1 — how close the sampled games are to this draft. */
    meanSimilarity: number
  }
  /** The lane at 15 minutes, over the same sampled games. */
  lane: CompositionLane
  build: {
    firstItemId: number
    buildTree: BuildTreeNode[]
    gamesConsidered: number
    wins: number
    runePage: BuildRunePage | null
    starterItems: BuildItemSet | null
    boots: BuildItemSet | null
    corePath: BuildItemSet | null
    summonerSpells: BuildSummonerSpells | null
    skillOrder: BuildSkillOrder | null
  }
}

/** Mirrors `CompositionLaneReadModel`. */
export interface CompositionLane {
  measuredGames: number
  decidedGames: number
  /** Share of decided lanes won; null when none were — never 0%. */
  winRate: number | null
  averageGoldDiffAt15: number | null
  averageXpDiffAt15: number | null
}
