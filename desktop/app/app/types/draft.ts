import type { DraftReasonReadModel } from '#shared/contract/api'
import type { ChampionPosition } from '#shared/utils/positions'

/** One card of the suggestion strip: a pick or a ban, and the facts behind it. */
export interface DraftSuggestionItem {
  championId: number
  reasons: DraftReasonReadModel[]
  /** Few games behind every reason. */
  thin: boolean
}

/** One slot of a team, as the draft screen hands it over. */
export interface TeamRow {
  championId: number | null
  lane: Lane | null
  /** A pick locked in; false while it is only hovered. */
  locked: boolean
  me?: boolean
}

/** A draft slot's position. The order, names and icons live in `#shared/utils/positions`. */
export type Lane = ChampionPosition
