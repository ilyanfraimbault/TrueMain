/**
 * The champion page's build, resolved to **names** server-side (#1123).
 *
 * Why a dedicated read model rather than reusing `ChampionResponse`: everything
 * the champion page shows is ids (item 3157, perk 8112), and turning those into
 * words needs `/api/static/items`, whose payload is ~373 KiB — deliberately
 * client-only (see `useStaticItems`). Server-rendering the page's build content
 * therefore cannot mean "SSR the existing fetches"; it means resolving the ids
 * away on the server and sending down only the handful of strings a crawler and
 * a first paint actually need. This model is that handful — ~1 KB, no icons, no
 * descriptions, no ids the client would have to look up again.
 *
 * Every field is nullable or empty-able on purpose. An id the static maps don't
 * know is **dropped**, never rendered as `Item 3157`: the whole point of the
 * block is to be the one part of the page a machine reads as prose, and a
 * placeholder that reads like a measurement is worse than a shorter sentence.
 */

/** A game entity the summary prints by name. `id` is kept for `:key` only. */
export interface SummaryEntity {
  id: number
  name: string
}

export interface ChampionBuildSummary {
  championId: number
  /** Null when DDragon is unavailable — the block then renders nothing. */
  championName: string | null
  /** Resolved lane, echoed from the aggregate (not from the request). */
  position: string | null
  patch: string | null
  /** `ALL`, a bare tier, or a `<TIER>_PLUS` form — echoed from the aggregate. */
  eloBracket: string
  games: number
  wins: number
  winRate: number
  /** Null when the slice holds no build at all (a champion with no aggregate). */
  build: ChampionBuildSummaryBuild | null
  /**
   * Distinct builds the aggregate kept for this slice, `build` included. A
   * count, not a list: naming every build would restate the tabs above it, but
   * how *many* ways the champion is built is a real property of the lane and
   * varies between champions.
   */
  buildCount: number
}

export interface ChampionBuildSummaryBuild {
  /** Games behind *this build*, always ≤ the slice's `games`. */
  games: number
  winRate: number
  pickRate: number
  summonerSpells: SummaryEntity[]
  starterItems: SummaryEntity[]
  boots: SummaryEntity | null
  /** Completed core items, in build order. */
  coreItems: SummaryEntity[]
  keystone: SummaryEntity | null
  primaryStyle: SummaryEntity | null
  secondaryStyle: SummaryEntity | null
  /** The three minor perks of the primary tree, in slot order. */
  primaryRunes: SummaryEntity[]
  secondaryRunes: SummaryEntity[]
  /**
   * Max priority, not a 1-18 grid: the aggregate only carries which of Q/W/E is
   * levelled first, so this is at most three entries (see `Core/SkillOrder.vue`).
   * `name` is null when the champion's spell list didn't resolve — the key alone
   * is still meaningful.
   */
  skills: Array<{ key: string, name: string | null }>
}
