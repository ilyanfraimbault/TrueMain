import type { GameflowPhase } from '~/types/lcu'

/**
 * The groupings of the client's phases, mirrored from `GameflowPhase` in
 * `crates/lcu/src/model.rs` (#2472): the one place the app names a set of
 * phases, so no two screens disagree on, say, a reconnect.
 */

/** The champion select. */
export const isChampSelect = (phase: GameflowPhase) => phase === 'ChampSelect'

/**
 * A game is on: being played, or crashed and offered to rejoin. Only
 * `InProgress` has the game's own API up — the Game screen opens on that
 * phase alone (`useLcuState`'s `screen`).
 */
export const isGameRunning = (phase: GameflowPhase) => phase === 'InProgress' || phase === 'Reconnect'

/** The game is over and the client is on its stats or the end-of-game screen. */
export const isPostGame = (phase: GameflowPhase) =>
  phase === 'WaitingForStats' || phase === 'PreEndOfGame' || phase === 'EndOfGame'
