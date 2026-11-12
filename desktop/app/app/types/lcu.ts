import { writable } from '~/utils/writable'
import type { AppView as AppState } from '~/generated/shell'
import { EMPTY_STATE as SHELL_EMPTY_STATE } from '~/generated/shell'

/** The app's state as the shell pushes it (`crates/shell-state`), generated from Rust. */
export type { AppView as AppState, DraftAction, DraftState, GameflowPhase, Screen, TeamSlot } from '~/generated/shell'

/** The state before the shell answers: no client. */
export const EMPTY_STATE: AppState = writable(SHELL_EMPTY_STATE)
