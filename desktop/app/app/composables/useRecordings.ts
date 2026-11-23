import type {
  RecordingLibrary,
  RecordingSettings,
  RecordingSettingsView,
  RecordingStatus,
} from '~/types/recordings'
import type { Chord } from '~/types/overlay'
import { RECORDING_SCHEME } from '~/generated/shell'
import type { ShellAnswer, ShellCommand, ShellCommands } from '~/utils/shell'
import { fakeShell, shell, shellEvents } from '~/utils/shell'

/** The shell's recording commands (`src-tauri/src/recording`). */
export type RecordingCommand = Extract<ShellCommand,
  | `recording${string}` | `clip${string}`
  | 'setRecordingSettings' | 'requestCapturePermission' | 'revealRecordingFile' | 'openYoutubeUpload'>

/**
 * Ask the shell to run one recording command (#1744).
 */
export async function recordingCall<K extends RecordingCommand>(command: K, ...args: Parameters<ShellCommands[K]>): Promise<ShellAnswer<K>> {
  return await (shell[command] as (...args: unknown[]) => Promise<ShellAnswer<K>>)(...args)
}

/** The shell's `convertFileSrc`, loaded once: a file on disk as a URL the webview may load. */
let convertFileSrc: ((path: string, protocol?: string) => string) | null = null

/** Settings writes, one after the other, and the latest one asked for. */
let settingsWrites: Promise<void> = Promise.resolve()
let settingsRequest = 0

/**
 * The player's recordings — full games and the clips cut from them — with the
 * recording settings and the capture status, as the shell reports them.
 *
 * Held for the session. The first caller (`app.vue`, which lives as long as
 * the window) subscribes to the shell's events: a library change reads the
 * library again, a status replaces the status, and a recap event opens the recap of the game that just stopped recording
 * — only from the dashboard or the game page, the pages the game's own
 * navigation would have taken the player to, never over a page they opened by
 * hand (the game page's rule, `decisions/desktop.md`).
 */
export function useRecordings() {
  const library = useState<RecordingLibrary | null>('recordings-library', () => null)
  const settings = useState<RecordingSettingsView | null>('recordings-settings', () => null)
  const status = useState<RecordingStatus | null>('recordings-status', () => null)
  const loadState = useState<'idle' | 'pending' | 'ready' | 'error'>('recordings-load', () => 'idle')
  const fileApiReady = useState<boolean>('recordings-file-api', () => false)
  const subscribed = useState<boolean>('recordings-subscribed', () => false)
  const toast = useToast()
  const router = useRouter()

  async function refreshLibrary() {
    try {
      library.value = await recordingCall('recordingLibrary')
    }
    catch {
      // The status read says why (or that this shell has no recording at all).
    }
  }

  async function refreshStatus() {
    status.value = await recordingCall('recordingStatus')
  }

  /** Read everything once. A shell without the recording commands leaves the status `null`. */
  async function load() {
    if (loadState.value === 'pending') return
    loadState.value = 'pending'
    try {
      const [view, current] = await Promise.all([
        recordingCall('recordingSettings'),
        recordingCall('recordingStatus'),
      ])
      settings.value = view
      status.value = current
      await refreshLibrary()
      loadState.value = 'ready'
    }
    catch {
      loadState.value = 'error'
    }
  }

  function openRecap(id: string) {
    const path = router.currentRoute.value.path
    if (path === '/' || path === '/game') void router.push(`/recordings/${encodeURIComponent(id)}`)
  }

  if (!subscribed.value) {
    subscribed.value = true
    onMounted(subscribe)
  }

  async function subscribe() {
    if (fakeShell) {
      // No shell to send the recap event: the console stands in for it.
      (window as unknown as Record<string, unknown>).__devRecordingRecap = (id: string) => {
        void refreshLibrary()
        openRecap(id)
      }
    }
    else {
      const core = await import('@tauri-apps/api/core')
      convertFileSrc = core.convertFileSrc
      fileApiReady.value = true
    }

    // Subscribed once, from app.vue, for the app's lifetime: there is no
    // scope to tie them to after the awaits above, and nothing to unsubscribe.
    await Promise.all([
      shellEvents.recordingStatus.listen(event => (status.value = event.payload)),
      shellEvents.recordingLibrary.listen(() => void refreshLibrary()),
      shellEvents.recordingRecap.listen((event) => {
        void refreshLibrary()
        openRecap(event.payload.id)
      }),
    ])

    // After subscribing, so nothing lands between the read and the listeners.
    await load()
  }

  /**
   * A file of the library as a URL: the shell's `recording` scheme — not Tauri's
   * asset protocol, whose short range answers lose a long game's picture (#1830) —
   * or the dev server's stand-in.
   */
  function fileSrc(path: string | null): string | null {
    if (!path) return null
    if (fileApiReady.value && convertFileSrc) return convertFileSrc(path, RECORDING_SCHEME)
    if (fakeShell) return `/__dev/recording-file?path=${encodeURIComponent(path)}`
    return null
  }

  /**
   * Run a command that changes the library, then read the library again.
   * `undefined` is a failure, already shown in a toast as the shell words it.
   */
  async function mutate<T>(write: () => Promise<T>, failure: string): Promise<T | undefined> {
    try {
      const answer = await write()
      await refreshLibrary()
      return answer
    }
    catch (error) {
      toast.add({ title: failure, description: String(error), color: 'error', icon: 'i-lucide-circle-alert' })
      return undefined
    }
  }

  const getRecording = (id: string) => recordingCall('recordingGet', id)
  /** The recap's curve data; `null` when the game has none (or it does not read — the recap stands without it). */
  const getWinProbability = (id: string) =>
    recordingCall('recordingWinProbability', id).catch(() => null)
  const setKept = (id: string, kept: boolean) =>
    mutate(() => recordingCall('recordingSetKept', id, kept), kept ? 'The game could not be kept' : 'The game could not be released')
  const deleteRecording = async (id: string) =>
    (await mutate(() => recordingCall('recordingDelete', id), 'The game could not be deleted')) !== undefined
  /** `replaces`: a saved clip of the same game this cut adjusts, deleted once the new one is written. */
  const saveClip = (recordingId: string, startMs: number, endMs: number, title: string, replaces?: string) =>
    mutate(() => recordingCall('clipSave', recordingId, startMs, endMs, title, replaces ?? null), 'The clip could not be saved')
  const checkMarkKey = (chord: Chord) => recordingCall('recordingCheckMarkKey', chord)
  const updateClip = (id: string, change: { title?: string, favorite?: boolean }) =>
    mutate(() => recordingCall('clipUpdate', id, change.title ?? null, change.favorite ?? null), 'The clip could not be updated')
  const deleteClip = async (id: string) =>
    (await mutate(() => recordingCall('clipDelete', id), 'The clip could not be deleted')) !== undefined

  async function reveal(path: string) {
    try {
      await recordingCall('revealRecordingFile', path)
    }
    catch (error) {
      toast.add({ title: 'The file could not be shown', description: String(error), color: 'error', icon: 'i-lucide-circle-alert' })
    }
  }

  /**
   * Shown at once and written in order: two quick changes (a resolution, then a
   * frame rate) each build on the one before, and only the last answer — the
   * shell's clamped values — is kept.
   */
  function saveSettings(next: RecordingSettings) {
    if (settings.value) settings.value = { ...settings.value, settings: next }
    const request = ++settingsRequest
    settingsWrites = settingsWrites.then(async () => {
      try {
        const answer = await recordingCall('setRecordingSettings', next)
        if (request === settingsRequest) settings.value = answer
        await Promise.all([refreshLibrary(), refreshStatus()])
      }
      catch (error) {
        toast.add({ title: 'The settings could not be saved', description: String(error), color: 'error', icon: 'i-lucide-circle-alert' })
        if (request === settingsRequest) settings.value = await recordingCall('recordingSettings').catch(() => settings.value)
      }
    })
    return settingsWrites
  }

  async function requestPermission() {
    try {
      status.value = await recordingCall('requestCapturePermission')
    }
    catch (error) {
      toast.add({ title: 'Screen Recording could not be requested', description: String(error), color: 'error', icon: 'i-lucide-circle-alert' })
    }
  }

  /**
   * Where "Watch" on a dashboard row goes: the full game's recap while it
   * exists, else the clips cut from it, else nowhere.
   */
  function watchTarget(gameId: number): string | null {
    const game = library.value?.games.find(recording => recording.gameId === gameId)
    if (game) return `/recordings/${encodeURIComponent(game.id)}`
    if (library.value?.clips.some(clip => clip.gameId === gameId)) return `/recordings?game=${gameId}`
    return null
  }

  return {
    library,
    settings,
    status,
    loadState,
    load,
    refreshLibrary,
    fileSrc,
    getRecording,
    getWinProbability,
    setKept,
    deleteRecording,
    saveClip,
    checkMarkKey,
    updateClip,
    deleteClip,
    reveal,
    saveSettings,
    requestPermission,
    watchTarget,
  }
}
