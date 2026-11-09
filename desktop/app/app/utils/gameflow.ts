import type { GameflowPhase } from '~/types/lcu'
import { PHASE_GROUPS } from '~/generated/shell'

/** The sets of client phases the shell names (`GameflowPhase::is_*`). */
const within = (group: readonly GameflowPhase[]) => (phase: GameflowPhase) => group.includes(phase)

export const isChampSelect = within(PHASE_GROUPS.champSelect)
/** A game is on: being played, or crashed and offered to rejoin. */
export const isGameRunning = within(PHASE_GROUPS.gameRunning)
/** The game is over and the client is on its stats or the end-of-game screen. */
export const isPostGame = within(PHASE_GROUPS.postGame)
