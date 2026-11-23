import type { ClipVideo } from '~/generated/shell'

/**
 * The recording contract between the shell and the webview (#1744, #1755,
 * #1777), generated from Rust (`crates/game-recording`,
 * `src-tauri/src/recording`). JSON is camelCase and every path is absolute on
 * the player's disk.
 */
export type {
  Activity as RecordingActivity,
  Availability as CaptureAvailability,
  AutoClipKind,
  ClipVideo,
  ClipView as Clip,
  DragonKind,
  FrameRate as RecordingFrameRate,
  GameView as GameRecording,
  LibraryView as RecordingLibrary,
  MarkLength,
  Moment,
  MomentKind,
  Queues as RecordingQueues,
  RecordingSettings,
  RecordingStatusView as RecordingStatus,
  Resolution as RecordingResolution,
  SettingsView as RecordingSettingsView,
  WinProbabilityView as RecordingWinProbability,
} from '~/generated/shell'

/** The clip's file is on this PC. */
export const videoOnDisk = (video: ClipVideo) => video.kind === 'local' || video.kind === 'both'

/** The YouTube video the clip was published as; null when it was not. */
export const youtubeUrl = (video: ClipVideo) => (video.kind === 'both' || video.kind === 'youtube' ? video.url : null)
