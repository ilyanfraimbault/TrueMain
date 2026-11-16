import type { CaptureAvailability, Clip, GameRecording, RecordingLibrary, RecordingSettings, RecordingSettingsView, RecordingStatus } from '~/types/recordings'
import { videoOnDisk, youtubeUrl } from '~/types/recordings'
import type { Chord } from '~/types/overlay'
import type { ShellAnswer, ShellCommands } from '~/utils/shell'
import type { RecordingCommand } from '~/composables/useRecordings'
import { cutClip, devChordView, devLibrary, devMarkKeyRefusal, devSettings, devStatus } from '~/fixtures/recordings'
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
  if (mode === 'empty' || mode === 'off') store.status.activity = { kind: 'idle' }
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

type Handlers = { [K in RecordingCommand]: (current: DevStore, ...args: Parameters<ShellCommands[K]>) => ShellAnswer<K> }

const handlers: Handlers = {
  recordingSettings: current => copy(current.settings),
  setRecordingSettings: (current, settings) => {
    const next = copy(settings) as RecordingSettings
    next.budgetBytes = Math.max(next.budgetBytes, current.settings.minBudgetBytes)
    if (next.markKey) {
      const refusal = devMarkKeyRefusal(next.markKey)
      if (refusal) throw new Error(refusal)
    }
    current.settings = { ...current.settings, settings: copy(next), markKey: next.markKey ? devChordView(next.markKey) : null }
    return copy(current.settings)
  },
  recordingCheckMarkKey: (_, chord) => ({ refusal: devMarkKeyRefusal(chord as Chord), chord: devChordView(chord as Chord) }),
  recordingStatus: current => copy(current.status),
  requestCapturePermission: (current) => {
    if (current.status.availability === 'permission') current.status = { ...current.status, availability: 'ready', message: null }
    return copy(current.status)
  },
  recordingLibrary: current => copy(libraryOf(current)),
  recordingGet: (current, id) => copy(game(current, id)),
  recordingWinProbability: (current, id) => {
    const found = game(current, id)
    return found.status === 'ready' ? copy(devWinProbability(found.gameId, found.durationMs)) : null
  },
  recordingSetKept: (current, id, kept) => {
    const found = game(current, id)
    found.kept = kept
    return copy(found)
  },
  recordingDelete: (current, id) => {
    game(current, id)
    current.games = current.games.filter(entry => entry.id !== id)
    current.clips = current.clips.map(entry => (entry.recordingId === id ? { ...entry, recordingId: null } : entry))
    return null
  },
  clipSave: (current, recordingId, rawStartMs, rawEndMs, title, replaces) => {
    const source = game(current, recordingId)
    // The shell cuts on the one-second keyframes; so does the stand-in.
    const startMs = Math.floor(rawStartMs / 1000) * 1000
    const endMs = Math.ceil(rawEndMs / 1000) * 1000
    if (endMs - startMs < 1000) throw new Error('A clip lasts at least a second.')
    // An adjusted range replaces its clip, keeping its favourite.
    const replaced = replaces ? clip(current, replaces) : null
    const saved = cutClip(source, source.id, startMs, endMs, title.trim() || 'Clip', Date.now(), replaced?.favorite)
    current.clips = [saved, ...current.clips.filter(entry => entry.id !== saved.id && entry.id !== replaced?.id)]
      .sort((a, b) => b.gameStartedAtMs - a.gameStartedAtMs || b.startMs - a.startMs)
    return copy(saved)
  },
  clipUpdate: (current, id, title, favorite) => {
    const found = clip(current, id)
    if (title !== null) found.title = title.trim() || found.title
    if (favorite !== null) found.favorite = favorite
    return copy(found)
  },
  clipDelete: (current, id) => {
    clip(current, id)
    current.clips = current.clips.filter(entry => entry.id !== id)
    return null
  },
  revealRecordingFile: (_, path) => {
    console.info('[dev] would reveal', path)
    return null
  },
  openYoutubeUpload: () => {
    console.info('[dev] would open YouTube\'s upload page')
    return null
  },
  clipOpenYoutube: (current, id) => {
    console.info('[dev] would open', youtubeUrl(clip(current, id).video))
    return null
  },
  clipStartDrag: (current, id) => {
    console.info('[dev] would drag', clip(current, id).videoPath)
    return null
  },
  clipDeleteVideo: (current, id) => {
    const found = clip(current, id)
    const url = youtubeUrl(found.video)
    if (!url) throw new Error('only a clip published on YouTube can leave this PC')
    found.video = { kind: 'youtube', url }
    found.sizeBytes = 64_000
    return copy(found)
  },
  clipYoutubeEmbed: (current, id) => {
    // The shell serves this page on the loopback (`embed.rs`); the dev server is already http.
    const videoId = new URL(youtubeUrl(clip(current, id).video) ?? 'https://x.invalid').searchParams.get('v')
    if (!videoId) throw new Error('this clip has no YouTube link')
    return `https://www.youtube-nocookie.com/embed/${videoId}?rel=0`
  },
  clipSetYoutubeUrl: (current, id, url) => {
    // The shell's parser (`youtube_video_url`), reduced to the two links YouTube hands out;
    // its check with YouTube's oEmbed (`check_video`) is the shell's alone.
    const found = clip(current, id)
    const link = url.trim()
    const onDisk = videoOnDisk(found.video)
    if (!link && !onDisk) throw new Error('this clip is only on YouTube now: its link is all that is left of it')
    const videoId = /^(?:https?:\/\/)?(?:www\.)?(?:youtu\.be\/|youtube\.com\/watch\?v=)([\w-]{11})(?:[?&#].*)?$/i.exec(link)?.[1]
    if (link && !videoId) throw new Error('this is not a link to a YouTube video')
    // As the shell's `ClipVideo::new`.
    const watch = videoId ? `https://www.youtube.com/watch?v=${videoId}` : null
    if (!watch) found.video = { kind: 'local' }
    else found.video = onDisk ? { kind: 'both', url: watch } : { kind: 'youtube', url: watch }
    return copy(found)
  },
}

export async function devRecordingCall<K extends RecordingCommand>(command: K, ...args: Parameters<ShellCommands[K]>): Promise<ShellAnswer<K>> {
  const current = open()
  // The shell answers over IPC; a short wait keeps loading states honest.
  await wait(command === 'clipSave' ? 1_200 : 120)
  return (handlers[command] as (current: DevStore, ...args: Parameters<ShellCommands[K]>) => ShellAnswer<K>)(current, ...args)
}
