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
 * `Core` — taken whatever the draft, or the only step its branch ever takes, so no situation
 * explains it.
 * `Situational` — its branch offers a real alternative and at least one situation measurably
 * moves the choice.
 * `Preference` — the branch offers a choice, and no situation moves it.
 */
export type ItemContextClass = 'Core' | 'Situational' | 'Preference'

export interface ChampionItemContextItem {
  slot: ItemContextSlot
  /**
   * The item this step followed — the branch of the build tree it belongs to (#1496). 0 on
   * the branch every build starts from, and on every boots and starter verdict, which are
   * decided before any item exists.
   */
  parentItemId: number
  itemId: number
  class: ItemContextClass
  games: number
  /** Games of the branch the rate is over: those that reached the parent and completed a further item. */
  branchGames: number
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
