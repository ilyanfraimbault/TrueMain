import { writable } from '~/utils/writable'
import type { AppState } from '~/generated/shell'
import { EMPTY_STATE as SHELL_EMPTY_STATE } from '~/generated/shell'

/** The app's state as the shell pushes it (`crates/shell-state`), generated from Rust. */
export type { AppState, DraftAction, DraftState, GameflowPhase, Screen, TeamSlot } from '~/generated/shell'

/** `AppState::default()`: no client yet. */
export const EMPTY_STATE: AppState = writable(SHELL_EMPTY_STATE)
