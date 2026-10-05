import type { CaptureAvailability, Clip, GameRecording, RecordingLibrary, RecordingSettings, RecordingSettingsView, RecordingStatus } from '~/types/recordings'
import { cutClip, devLibrary, devSettings, devStatus } from '~/fixtures/recordings'
import { devWinProbability } from '~/fixtures/recording-win-probability'

/**
 * The recording commands, answered in memory for `npm run dev` in a browser —
 * a stand-in for the shell, never part of a build (`useRecordings` imports it
 * behind `import.meta.dev`). Commands mutate the fixtures, so saving a clip,
 * keeping or deleting a game and renaming all show on the pages until a
 * reload. The videos are served by `server/routes/__dev/recording-file.ts`.
 */

interface DevStore {
  games: GameRecording[]
  clips: Clip[]
  settings: RecordingSettingsView
  status: RecordingStatus
}

let store: DevStore | null = null

function open(): DevStore {
  if (store) return store
  const mode = new URLSearchParams(window.location.search).get('recordings')
  const { games, clips } = mode === 'empty' || mode === 'off' ? { games: [], clips: [] } : devLibrary()
  const settings = devSettings()
  if (mode === 'off') settings.settings.enabled = false
  const availability: CaptureAvailability = mode === 'permission' || mode === 'unsupported' || mode === 'missing' ? mode : 'ready'
  store = { games, clips, settings, status: devStatus(availability) }
  if (mode === 'empty' || mode === 'off') store.status.processingGameId = null
  return store
}

const wait = (ms: number) => new Promise(resolve => setTimeout(resolve, ms))

function libraryOf(current: DevStore): RecordingLibrary {
  return {
    games: [...current.games],
    clips: [...current.clips],
    usedBytes: [...current.games, ...current.clips].reduce((sum, entry) => sum + entry.sizeBytes, 0),
    budgetBytes: current.settings.settings.budgetBytes,
    folder: current.settings.folder,
  }
}

function game(current: DevStore, id: string): GameRecording {
  const found = current.games.find(entry => entry.id === id)
  if (!found) throw new Error(`No recording ${id}.`)
  return found
}

function clip(current: DevStore, id: string): Clip {
  const found = current.clips.find(entry => entry.id === id)
  if (!found) throw new Error(`No clip ${id}.`)
  return found
}

const copy = <T>(value: T): T => JSON.parse(JSON.stringify(value))

export async function devRecordingCall(command: string, args: Record<string, unknown> = {}): Promise<unknown> {
  const current = open()
  // The shell answers over IPC; a short wait keeps loading states honest.
  await wait(command === 'clip_save' ? 1_200 : 120)

  switch (command) {
    case 'recording_settings': return copy(current.settings)
    case 'set_recording_settings': {
      const next = args.settings as RecordingSettings
      next.budgetBytes = Math.max(next.budgetBytes, current.settings.minBudgetBytes)
      current.settings = { ...current.settings, settings: copy(next) }
      return copy(current.settings)
    }
    case 'recording_status': return copy(current.status)
    case 'request_capture_permission':
      if (current.status.availability === 'permission') current.status = { ...current.status, availability: 'ready', message: null }
      return copy(current.status)
    case 'recording_library': return copy(libraryOf(current))
    case 'recording_get': return copy(game(current, String(args.id)))
    case 'recording_win_probability': {
      const found = game(current, String(args.id))
      return found.status === 'ready' ? copy(devWinProbability(found.gameId, found.durationMs)) : null
    }
    case 'recording_set_kept': {
      const found = game(current, String(args.id))
      found.kept = Boolean(args.kept)
      return copy(found)
    }
    case 'recording_delete':
      game(current, String(args.id))
      current.games = current.games.filter(entry => entry.id !== args.id)
      current.clips = current.clips.map(entry => (entry.recordingId === args.id ? { ...entry, recordingId: null } : entry))
      return null
    case 'clip_save': {
      const source = game(current, String(args.recordingId))
      // The shell cuts on the one-second keyframes; so does the stand-in.
      const startMs = Math.floor(Number(args.startMs) / 1000) * 1000
      const endMs = Math.ceil(Number(args.endMs) / 1000) * 1000
      if (endMs - startMs < 1000) throw new Error('A clip lasts at least a second.')
      const saved = cutClip(source, source.id, startMs, endMs, String(args.title).trim() || 'Clip', Date.now())
      current.clips = [saved, ...current.clips.filter(entry => entry.id !== saved.id)]
        .sort((a, b) => b.gameStartedAtMs - a.gameStartedAtMs || b.startMs - a.startMs)
      return copy(saved)
    }
    case 'clip_update': {
      const found = clip(current, String(args.id))
      if (typeof args.title === 'string') found.title = args.title.trim() || found.title
      if (typeof args.favorite === 'boolean') found.favorite = args.favorite
      return copy(found)
    }
    case 'clip_delete':
      clip(current, String(args.id))
      current.clips = current.clips.filter(entry => entry.id !== args.id)
      return null
    case 'reveal_recording_file':
      console.info('[dev] would reveal', args.path)
      return null
    default:
      throw new Error(`Unknown recording command ${command}.`)
  }
}
