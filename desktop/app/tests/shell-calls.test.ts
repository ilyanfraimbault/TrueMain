import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref, type Ref } from 'vue'

/**
 * The composables inside the app, against a stand-in for the shell's IPC:
 * each feature reaches the command the generated bindings name, with the
 * arguments the shell reads, and handles the shell's answer or refusal.
 */

const ipc = vi.hoisted(() => ({
  invoke: vi.fn(),
  listeners: new Map<string, (event: { payload: unknown }) => void>(),
}))
vi.mock('@tauri-apps/api/core', () => ({ invoke: ipc.invoke, convertFileSrc: (path: string, scheme: string) => `${scheme}://${path}` }))
vi.mock('@tauri-apps/api/event', () => ({
  listen: vi.fn(async (name: string, handler: (event: { payload: unknown }) => void) => {
    ipc.listeners.set(name, handler)
    return () => ipc.listeners.delete(name)
  }),
}))

let states: Map<string, Ref<unknown>>
let toasts: { title: string, description?: string }[]

const flush = () => new Promise(resolve => setTimeout(resolve, 0))
const called = () => ipc.invoke.mock.calls.map(([name, args]) => [name, args])

beforeEach(() => {
  vi.resetModules()
  states = new Map()
  toasts = []
  ipc.invoke.mockReset()
  ipc.listeners.clear()
  vi.stubGlobal('ref', ref)
  vi.stubGlobal('computed', computed)
  vi.stubGlobal('onMounted', (run: () => unknown) => void run())
  vi.stubGlobal('onScopeDispose', () => {})
  vi.stubGlobal('useState', (key: string, init: () => unknown) => {
    if (!states.has(key)) states.set(key, ref(init()))
    return states.get(key)
  })
  vi.stubGlobal('insideTauri', () => true)
  vi.stubGlobal('useToast', () => ({ add: (toast: { title: string }) => toasts.push(toast) }))
  vi.stubGlobal('useRouter', () => ({ currentRoute: ref({ path: '/' }), push: vi.fn() }))
})

afterEach(() => vi.unstubAllGlobals())

describe('the overlay settings', () => {
  it('follow the shell, and save, preview, check, read binds and fit through it', async () => {
    const { DEV_OVERLAY_VIEW } = await import('~/types/overlay')
    ipc.invoke.mockImplementation(async (name: string) => (name === 'overlay_check_trigger' ? { refusal: null, chord: null } : DEV_OVERLAY_VIEW))
    const { useGameOverlay } = await import('~/composables/useGameOverlay')
    const overlay = useGameOverlay()
    await flush()
    expect(overlay.view.value).toEqual(DEV_OVERLAY_VIEW)

    await overlay.save({ opacity: 0.7 })
    await overlay.preview(true)
    await overlay.check('stats', { kind: 'always' })
    await overlay.readBinds()
    await overlay.fit('stats', 200, 80)
    expect(called().map(([name]) => name)).toEqual([
      'overlay_view',
      'set_overlay_settings',
      'overlay_preview',
      'overlay_check_trigger',
      'overlay_read_binds',
      'overlay_fit',
    ])
    expect(called()[1]![1]).toMatchObject({ settings: { opacity: 0.7 } })
    expect(called()[5]![1]).toEqual({ panel: 'stats', width: 200, height: 80 })

    ipc.listeners.get('overlay://view')!({ payload: { ...DEV_OVERLAY_VIEW, preview: true } })
    expect(overlay.view.value?.preview).toBe(true)
  })
})

describe('the recordings', () => {
  it('reach each recording command by its name and arguments', async () => {
    ipc.invoke.mockResolvedValue(null)
    const { recordingCall } = await import('~/composables/useRecordings')
    await recordingCall('clipSave', 'game-1', 1000, 5000, 'Clip', null)
    await recordingCall('clipUpdate', 'clip-1', null, true)
    expect(called()).toEqual([
      ['clip_save', { recordingId: 'game-1', startMs: 1000, endMs: 5000, title: 'Clip', replaces: null }],
      ['clip_update', { id: 'clip-1', title: null, favorite: true }],
    ])
  })

  it('publish a clip through the shell, and say its refusals', async () => {
    ipc.invoke.mockImplementation(async (name: string) => {
      if (name === 'clip_delete_video') throw 'only a clip published on YouTube can leave this PC'
      if (name === 'clip_set_youtube_url') throw 'this is not a link to a YouTube video'
      if (name === 'clip_youtube_embed') return 'http://127.0.0.1/embed'
      return null
    })
    const { useRecordings } = await import('~/composables/useRecordings')
    vi.stubGlobal('useRecordings', useRecordings)
    const { useClipPublish } = await import('~/composables/useClipPublish')
    const publish = useClipPublish()

    await publish.openUpload()
    await publish.openVideo('clip-1')
    await publish.startDrag('clip-1')
    expect(await publish.setYoutubeUrl('clip-1', 'nope')).toBe('This is not a link to a YouTube video')
    expect(await publish.deleteVideo('clip-1')).toBe(false)
    expect(await publish.embedUrl('clip-1')).toBe('http://127.0.0.1/embed')
    const reads = new Set(['recording_settings', 'recording_status', 'recording_library'])
    expect(called().map(([name]) => name).filter(name => !reads.has(name))).toEqual([
      'open_youtube_upload',
      'clip_open_youtube',
      'clip_start_drag',
      'clip_set_youtube_url',
      'clip_delete_video',
      'clip_youtube_embed',
    ])
    expect(toasts.map(toast => toast.title)).toEqual(['The video could not be removed'])
  })

  it('subscribe to the shell, then read the library, and serve files over its scheme', async () => {
    ipc.invoke.mockImplementation(async (name: string) => (name === 'recording_library'
      ? { games: [], clips: [], usedBytes: 0, budgetBytes: 1, folder: '/r' }
      : { availability: 'ready', message: null, activity: { kind: 'idle' } }))
    const { useRecordings } = await import('~/composables/useRecordings')
    const recordings = useRecordings()
    await flush()
    await flush()
    expect([...ipc.listeners.keys()].sort()).toEqual(['recording://library', 'recording://recap', 'recording://status'])
    expect(recordings.fileSrc('/r/a.mp4')).toBe('recording:///r/a.mp4')

    ipc.listeners.get('recording://status')!({ payload: { availability: 'missing', message: 'gone', activity: { kind: 'idle' } } })
    expect(recordings.status.value?.availability).toBe('missing')
  })
})

describe('the recordings library', () => {
  it('runs each change through the shell, then reads the library again', async () => {
    ipc.invoke.mockImplementation(async (name: string) => {
      if (name === 'recording_delete') throw 'this recording is no longer on disk'
      if (name === 'recording_library') return { games: [], clips: [], usedBytes: 0, budgetBytes: 1, folder: '/r' }
      if (name === 'recording_status' || name === 'request_capture_permission') return { availability: 'ready', message: null, activity: { kind: 'idle' } }
      if (name === 'recording_win_probability') throw 'unreadable'
      return { id: 'x' }
    })
    vi.stubGlobal('onMounted', () => {})
    const { useRecordings } = await import('~/composables/useRecordings')
    const recordings = useRecordings()

    await recordings.getRecording('game-1')
    expect(await recordings.getWinProbability('game-1')).toBeNull()
    await recordings.setKept('game-1', true)
    expect(await recordings.deleteRecording('game-1')).toBe(false)
    await recordings.saveClip('game-1', 0, 1000, 'Clip')
    await recordings.checkMarkKey({ alt: true, shift: false, ctrl: false, meta: false, tab: false, key: 'KeyM' })
    await recordings.updateClip('clip-1', { title: 'Renamed' })
    await recordings.deleteClip('clip-1')
    await recordings.reveal('/r/a.mp4')
    await recordings.requestPermission()

    expect(called().map(([name]) => name).filter(name => name !== 'recording_library')).toEqual([
      'recording_get',
      'recording_win_probability',
      'recording_set_kept',
      'recording_delete',
      'clip_save',
      'recording_check_mark_key',
      'clip_update',
      'clip_delete',
      'reveal_recording_file',
      'request_capture_permission',
    ])
    expect(called().find(([name]) => name === 'clip_update')![1]).toEqual({ id: 'clip-1', title: 'Renamed', favorite: null })
    expect(toasts.map(toast => toast.title)).toEqual(['The game could not be deleted'])
  })
})

describe('the live game', () => {
  it('reads the game after listening, and applies the updates in order', async () => {
    const game = { revision: 1, gameTime: 60, players: [] }
    ipc.invoke.mockResolvedValue(game)
    const { useLiveGame } = await import('~/composables/useLiveGame')
    const live = useLiveGame()
    await flush()
    expect(live.game.value).toMatchObject({ revision: 1 })

    ipc.listeners.get('game://update')!({ payload: { revision: 2, gameTime: 62, changes: [] } })
    expect(live.game.value).toMatchObject({ revision: 2, gameTime: 62 })
    ipc.listeners.get('game://snapshot')!({ payload: null })
    expect(live.game.value).toBeNull()
  })
})

describe('the shell\'s reads of TrueMain', () => {
  it('ask the shell, and hand a status back as the shared pages read one', async () => {
    vi.stubGlobal('trackLoad', (work: Promise<unknown>) => work)
    ipc.invoke.mockImplementation(async (name: string, args: { path: string }) => {
      if (args.path === '/players/nobody') throw 'TrueMain answered 404 Not Found for /players/nobody'
      return { ok: name }
    })
    const { apiGet, apiPost, useApiFetch } = await import('~/composables/useApi')
    expect(await apiGet('/truemains', { page: 2, empty: '' })).toEqual({ ok: 'api_get' })
    expect(await apiPost('/champions/7/next-item', { gold: 1 })).toEqual({ ok: 'api_post' })
    expect(called()[0]).toEqual(['api_get', { path: '/truemains', query: [['page', '2']] }])
    expect(called()[1]).toEqual(['api_post', { path: '/champions/7/next-item', query: [], request: { gold: 1 } }])
    await expect(useApiFetch()('/players/nobody')).rejects.toMatchObject({ statusCode: 404 })
    expect(await useApiFetch()('/players/nobody', { ignoreResponseError: true })).toBeNull()
  })
})

describe('the rune import', () => {
  it('writes the page through the shell, and says a full book', async () => {
    vi.stubGlobal('useLcuState', () => ({ state: ref({ connected: true }) }))
    ipc.invoke.mockRejectedValueOnce('no-slot').mockResolvedValueOnce(null)
    const { useRuneImport } = await import('~/composables/useRuneImport')
    const runes = useRuneImport()
    const page = { primaryStyleId: 8100, primaryKeystoneId: 8112, primaryPerk1Id: 1, primaryPerk2Id: 2, primaryPerk3Id: 3, secondaryStyleId: 8200, secondaryPerk1Id: 4, secondaryPerk2Id: 5, statOffense: 6, statFlex: 7, statDefense: 8 }
    await runes.importRunes(page, 'Ahri')
    await runes.importRunes(page, 'Ahri')
    expect(called()[0]).toEqual(['import_runes', { champion: 'Ahri', page }])
    expect(toasts.map(toast => toast.title)).toEqual(['No free rune page', 'Runes imported'])
  })
})
