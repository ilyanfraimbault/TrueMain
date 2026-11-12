import { writable } from '~/utils/writable'
import type { LoadingView, Read } from '~/generated/shell'
import { IDLE_LOADING as SHELL_IDLE_LOADING } from '~/generated/shell'

/** The loading screen's board as the shell reads it (`src-tauri/src/loading`), generated from Rust. */
export type {
  LaningForm,
  LoadingPlayer,
  LoadingStatus,
  LoadingView,
  PlayerForm,
  PositionGames,
  Read,
  RecentGame,
} from '~/generated/shell'

/** A player whose name and history are read. */
export type KnownPlayer = Extract<LoadingView['players'][number], { kind: 'known' }>
/** A player who hides their name (Streamer Mode): only their champion and lane show. */
export type HiddenPlayer = Extract<LoadingView['players'][number], { kind: 'hidden' }>

/** `LoadingView::default()`: no game loading. */
export const IDLE_LOADING: LoadingView = writable(SHELL_IDLE_LOADING)

/** The value of a read once it is in; null while pending or failed. */
export function readValue<T>(read: Read<T>): T | null {
  return read.state === 'ok' ? read.value : null
}
