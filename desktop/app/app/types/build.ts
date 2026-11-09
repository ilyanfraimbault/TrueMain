import type { BuildItemSetReadModel, BuildRunePageReadModel, BuildSkillOrderReadModel, BuildSummonerSpellsReadModel, BuildTreeNodeReadModel } from '#shared/contract/api'

/** The six blocks of the core view, whichever endpoint they came from. */
export interface BuildCoreView {
  itemPath: BuildItemSetReadModel | null
  boots: BuildItemSetReadModel | null
  starterItems: BuildItemSetReadModel | null
  summonerSpells: BuildSummonerSpellsReadModel | null
  skillOrder: BuildSkillOrderReadModel | null
  runePage: BuildRunePageReadModel | null
}

/**
 * One build a build view can show — the one computed for the draft, or one of
 * the champion's lane builds — in the shape the view draws, whichever
 * endpoint it came from.
 */
export interface BuildOption {
  /** `draft`, or `lane-{firstItemId}-{keystoneId}` — the site groups lane builds by both. */
  key: string
  core: BuildCoreView
  firstItemId: number
  keystoneId: number | null
  buildTree: BuildTreeNodeReadModel[]
  games: number
  winRate: number | null
  /** The draft's build fell back to the lane's standard one: the matchup was never recorded. */
  standard?: boolean
}

/**
 * The draft's `POST /champions/{id}/composition-build` body as the app builds it: every pick
 * seated, every field set — the contract's `CompositionBuildRequest` with nothing left optional.
 */

/** One known pick of the draft. */
export interface CompositionSlot {
  championId: number
  position: string
}

export interface CompositionBuildRequest {
  position: string
  allies: CompositionSlot[]
  enemies: CompositionSlot[]
  /** A Riot ID: only that player's games are sampled — a true main's own draft build (#1987). */
  player?: string
}
