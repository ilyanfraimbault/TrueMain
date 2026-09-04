/**
 * The situational build context of a champion slice (#1450, read surface #1451) —
 * `GET /api/champions/{id}/item-context`.
 *
 * Kept out of `champions.ts`: that file is the champion page's whole contract and is
 * already over the size the repo ratchets on, while this is one endpoint answering one
 * question. The wording of every axis lives next door, in `shared/utils/item-context.ts`.
 */
export interface ChampionItemContextResponse {
  championId: number
  position: string
  patch: string | null
  allRanks: boolean
  items: ChampionItemContextItem[]
}

/** `Build` is a completed legendary, `Boots` the tier-two boots, `Starter` one starting item. */
export type ItemContextSlot = 'Build' | 'Boots' | 'Starter'

/**
 * `Core` — built whatever the draft, so no situation explains it.
 * `Situational` — at least one situation measurably moves it.
 * `Preference` — built often enough to matter, and no situation moves it.
 */
export type ItemContextClass = 'Core' | 'Situational' | 'Preference'

export interface ChampionItemContextItem {
  slot: ItemContextSlot
  itemId: number
  class: ItemContextClass
  games: number
  slotGames: number
  pickRate: number
  winRate: number | null
  /** Widest patch window behind the findings — 1 when this patch carried them alone. */
  patchWindow: number
  axes: ChampionItemContextAxis[]
}

export interface ChampionItemContextAxis {
  /** e.g. `EnemyMagicDamage`. The wording lives in `shared/utils/item-context`. */
  axis: string
  /** The end of the axis where the item is picked more. */
  bucket: 'High' | 'Low'
  /** False for the gold-lead axis: advice a reader can only act on once the game is under way. */
  draftTime: boolean
  gamesIn: number
  totalIn: number
  gamesOut: number
  totalOut: number
  rateIn: number
  rateOut: number
  lift: number
  patchWindow: number
}
