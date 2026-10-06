import type { WinProbabilityTimeline } from '#shared/types/win-probability'

/**
 * The recording contract between the shell and the webview (#1744, #1755, #1777),
 * verbatim. Mirrors the shell's recording commands and events; JSON is camelCase
 * and every path is absolute on the player's disk.
 */
export type RecordingResolution = 'native' | '1440p' | '1080p' | '720p'
export type RecordingFrameRate = 30 | 60
export type RecordingQueues = 'ranked' | 'all'

/** What the player chooses. `folder: null` = the default folder. */
export interface RecordingSettings {
  enabled: boolean
  queues: RecordingQueues
  quality: { resolution: RecordingResolution, frameRate: RecordingFrameRate }
  budgetBytes: number
  folder: string | null
}

export interface RecordingSettingsView {
  settings: RecordingSettings
  /** The folder in use (the chosen one, else the default). */
  folder: string
  minBudgetBytes: number
  /** Estimated size of an hour at each preset, for a 16:9 window at least that tall. `null` for native. */
  estimates: { resolution: RecordingResolution, frameRate: RecordingFrameRate, bytesPerHour: number | null }[]
}

/**
 * ready: the helper is there and Screen Recording is allowed.
 * permission: Screen Recording is not allowed for the app (macOS).
 * unsupported: no capture helper on this platform (Linux), or a Windows too
 *   old for window capture (before 10 1903).
 * missing: the helper should be there and is not (a broken install).
 */
export type CaptureAvailability = 'ready' | 'permission' | 'unsupported' | 'missing'

export interface RecordingStatus {
  availability: CaptureAvailability
  /** The helper's own words when not ready. */
  message: string | null
  /** The game being recorded right now. */
  recordingGameId: number | null
  /** A stopped game whose recording waits for the match history (highlights not final yet). */
  processingGameId: number | null
}

export type MomentKind =
  | 'kill' | 'death' | 'assist'
  | 'dragon' | 'elder' | 'baron' | 'herald' | 'grubs' | 'atakhan' | 'tower' | 'inhibitor'

/** One moment on a video's timeline, already mapped onto the video. */
export interface Moment {
  kind: MomentKind
  /** Video time of the moment, ms from the start of the file it is listed with. */
  videoMs: number
  /** Last kill of a multi-kill; = videoMs otherwise. */
  endVideoMs: number
  /** Kills in a row (1–5) for a kill, 0 otherwise. */
  kills: number
  /** Objectives only: whether the player's team took it. `null` for kills, deaths, assists. */
  ally: boolean | null
}

/** One full game on disk. */
export interface GameRecording {
  /** The folder's name — stable id for the commands. */
  id: string
  gameId: number
  queueId: number
  startedAtMs: number
  status: 'recording' | 'processing' | 'ready'
  durationMs: number | null
  /** The player's line, once the match history has the game. */
  championId: number | null
  win: boolean | null
  kills: number | null
  deaths: number | null
  assists: number | null
  /** Kept by the player: exempt from the disk budget. */
  kept: boolean
  sizeBytes: number
  videoPath: string
  thumbnailPath: string | null
  /** Empty when the game clock could not be anchored. */
  moments: Moment[]
  highlightsSource: 'timeline' | 'live' | null
}

/**
 * What the recap draws a full game's win-probability curve from (#1911),
 * `recording_win_probability`: kept beside the video when the recording was
 * finalised, so it outlives the game's place in the client's history. `null`
 * for a game that has none — its timeline never read (a live-feed-only
 * recording), or its clock could not be tied to the video.
 */
export interface RecordingWinProbability {
  /** The player's side, 100 or 200: the curve reads for it. */
  teamId: number
  /** Every participant's champion, to name them in a turning point. */
  champions: { participantId: number, championId: number }[]
  /** The game's timeline reduced to what the shared model reads. */
  timeline: WinProbabilityTimeline
  /** The game-clock anchor's segments, ordered: game time + `offsetMs` is video time from `fromGameMs` on. Never empty. */
  clock: { fromGameMs: number, offsetMs: number }[]
  durationMs: number | null
}

/** One clip the player saved: its own file, outliving the full game. */
export interface Clip {
  id: string
  title: string
  gameId: number
  /** The full game it was cut from, while that still exists. */
  recordingId: string | null
  queueId: number
  championId: number | null
  win: boolean | null
  /** The game's K/D/A, as on the card. */
  kills: number | null
  deaths: number | null
  assists: number | null
  gameStartedAtMs: number
  createdAtMs: number
  /** Where in the full game it was cut from, ms. */
  startMs: number
  endMs: number
  durationMs: number
  favorite: boolean
  sizeBytes: number
  videoPath: string
  thumbnailPath: string | null
  /** The moments inside it, relative to the clip's own start. */
  moments: Moment[]
}

export interface RecordingLibrary {
  /** Newest first. */
  games: GameRecording[]
  /** Newest first. */
  clips: Clip[]
  /** Full games and clips together. */
  usedBytes: number
  budgetBytes: number
  folder: string
}
