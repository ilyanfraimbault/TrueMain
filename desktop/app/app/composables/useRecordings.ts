import type {
  Clip,
  GameRecording,
  RecordingLibrary,
  RecordingSettings,
  RecordingSettingsView,
  RecordingStatus,
} from '~/types/recordings'

/**
 * Ask the shell to run one recording command (#1744). In `npm run dev` outside
 * Tauri the dev fixtures answer instead (`utils/recordings-dev.ts`), mutating
 * an in-memory library so the pages can be exercised; in a production build
 * outside Tauri there is nothing to ask.
 */
export async function recordingCall<T>(command: string, args?: Record<string, unknown>): Promise<T> {
  if (insideTauri()) {
    const { invoke } = await import('@tauri-apps/api/core')
    return await invoke<T>(command, args)
  }
  if (import.meta.dev) {
    const { devRecordingCall } = await import('~/utils/recordings-dev')
    return await devRecordingCall(command, args) as T
  }
  throw new Error('Recording is only available in the desktop app.')
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
 * the window) subscribes to the shell's events: `recording://library` reads the
 * library again, `recording://status` replaces the status, and
 * `recording://recap` opens the recap of the game that just stopped recording
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
      library.value = await recordingCall<RecordingLibrary>('recording_library')
    }
    catch {
      // The status read says why (or that this shell has no recording at all).
    }
  }

  async function refreshStatus() {
    status.value = await recordingCall<RecordingStatus>('recording_status')
  }

  /** Read everything once. A shell without the recording commands leaves the status `null`. */
  async function load() {
    if (loadState.value === 'pending') return
    loadState.value = 'pending'
    try {
      const [view, current] = await Promise.all([
        recordingCall<RecordingSettingsView>('recording_settings'),
        recordingCall<RecordingStatus>('recording_status'),
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
    if (!insideTauri()) {
      if (import.meta.dev) {
        // No shell to send `recording://recap`: the console stands in for it.
        (window as unknown as Record<string, unknown>).__devRecordingRecap = (id: string) => {
          void refreshLibrary()
          openRecap(id)
        }
        await load()
      }
      return
    }

    const core = await import('@tauri-apps/api/core')
    convertFileSrc = core.convertFileSrc
    fileApiReady.value = true

    // Subscribed once, from app.vue, for the app's lifetime: there is no
    // scope to tie them to after the awaits above, and nothing to unsubscribe.
    const { listen } = await import('@tauri-apps/api/event')
    await Promise.all([
      listen<RecordingStatus>('recording://status', event => (status.value = event.payload)),
      listen<null>('recording://library', () => void refreshLibrary()),
      listen<{ id: string }>('recording://recap', (event) => {
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
    if (fileApiReady.value && convertFileSrc) return convertFileSrc(path, 'recording')
    if (import.meta.dev && !insideTauri()) return `/__dev/recording-file?path=${encodeURIComponent(path)}`
    return null
  }

  /**
   * Run a command that changes the library, then read the library again.
   * `undefined` is a failure, already shown in a toast as the shell words it.
   */
  async function mutate<T>(command: string, args: Record<string, unknown>, failure: string): Promise<T | undefined> {
    try {
      const answer = await recordingCall<T>(command, args)
      await refreshLibrary()
      return answer
    }
    catch (error) {
      toast.add({ title: failure, description: String(error), color: 'error', icon: 'i-lucide-circle-alert' })
      return undefined
    }
  }

  const getRecording = (id: string) => recordingCall<GameRecording>('recording_get', { id })
  const setKept = (id: string, kept: boolean) =>
    mutate<GameRecording>('recording_set_kept', { id, kept }, kept ? 'The game could not be kept' : 'The game could not be released')
  const deleteRecording = async (id: string) =>
    (await mutate<null>('recording_delete', { id }, 'The game could not be deleted')) !== undefined
  const saveClip = (recordingId: string, startMs: number, endMs: number, title: string) =>
    mutate<Clip>('clip_save', { recordingId, startMs, endMs, title }, 'The clip could not be saved')
  const updateClip = (id: string, change: { title?: string, favorite?: boolean }) =>
    mutate<Clip>('clip_update', { id, ...change }, 'The clip could not be updated')
  const deleteClip = async (id: string) =>
    (await mutate<null>('clip_delete', { id }, 'The clip could not be deleted')) !== undefined

  async function reveal(path: string) {
    try {
      await recordingCall<null>('reveal_recording_file', { path })
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
        const answer = await recordingCall<RecordingSettingsView>('set_recording_settings', { settings: next })
        if (request === settingsRequest) settings.value = answer
        await Promise.all([refreshLibrary(), refreshStatus()])
      }
      catch (error) {
        toast.add({ title: 'The settings could not be saved', description: String(error), color: 'error', icon: 'i-lucide-circle-alert' })
        if (request === settingsRequest) settings.value = await recordingCall<RecordingSettingsView>('recording_settings').catch(() => settings.value)
      }
    })
    return settingsWrites
  }

  async function requestPermission() {
    try {
      status.value = await recordingCall<RecordingStatus>('request_capture_permission')
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
    setKept,
    deleteRecording,
    saveClip,
    updateClip,
    deleteClip,
    reveal,
    saveSettings,
    requestPermission,
    watchTarget,
  }
}
