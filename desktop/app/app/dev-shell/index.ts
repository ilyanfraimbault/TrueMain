import type { ApiError, GameDetail, KitStore, OverlaySettings, OverlayView, PlayerRecord } from '~/generated/shell'
import type { ShellAnswer, ShellCommands, ShellEvents } from '~/utils/shell'
import { DEV_OVERLAY_VIEW, OVERLAY_PANELS, PANEL_KEY } from '~/types/overlay'
import { chordLabel, chordOfTrigger } from '~/utils/overlay-keys'
import { EMPTY_STORE, readStore } from '~/utils/kit'
import { devRecordingCall } from '~/utils/recordings-dev'
import { devLoadingView, devTruemainMarks } from '~/utils/loading-dev'

/**
 * The shell, faked for `npm run dev` in a browser (`utils/shell.ts` installs
 * it): every command the real shell has, answered from the dev scenarios
 * (`useDevScenarios`), the fixtures and the browser, so the app runs whole
 * with no client and no Rust. Typed by the generated bindings: a command added
 * to the shell needs its answer here, or this file does not compile.
 *
 * Dev only by construction — `utils/shell.ts` imports it behind
 * `import.meta.dev`, which drops it from a production build. It proves nothing
 * below the frontend: the shell's own rules are exercised by its tests and by
 * a tape (README, "Testing without a game").
 */

type Handlers = { [K in keyof ShellCommands]: (...args: Parameters<ShellCommands[K]>) => ShellAnswer<K> | Promise<ShellAnswer<K>> }

/** Where the dev server keeps kits, with no shell to keep them. */
const KITS_KEY = 'truemain:kits:v1'

const copy = <T>(value: T): T => JSON.parse(JSON.stringify(value))

/** The scenario named in the URL, loaded once, on the first read of the state. */
let scenarioLoaded: Promise<void> | null = null
function scenarios() {
  const dev = useDevScenarios()
  scenarioLoaded ??= dev.load().then(() => dev.select(new URLSearchParams(window.location.search).get('scenario') ?? ''))
  return { dev, ready: scenarioLoaded }
}

/** A TrueMain read through the dev server's proxy (`nuxt.config.ts`), failing as the shell fails. */
async function readThroughProxy(method: 'GET' | 'POST', path: string, query: [string, string][], body?: unknown): Promise<unknown> {
  const search = new URLSearchParams(query)
  try {
    return await $fetch(`/api${path}${search.size ? `?${search}` : ''}`, { method, body: body as Record<string, unknown> | undefined })
  }
  catch (cause) {
    const status = (cause as { statusCode?: unknown }).statusCode
    const error: ApiError = typeof status === 'number'
      ? { kind: 'answered', status, message: `TrueMain answered ${status} for ${path}` }
      : { kind: 'unreachable', message: `could not reach TrueMain: ${String(cause)}` }
    throw error
  }
}

/** The chord labels the shell would answer, with no game binds to warn about. */
function devChords(settings: OverlaySettings): OverlayView['chords'] {
  const chords: OverlayView['chords'] = {}
  for (const panel of OVERLAY_PANELS) {
    const chord = chordOfTrigger(settings[PANEL_KEY[panel]].trigger)
    if (chord) chords[panel] = { label: chordLabel(chord, true), warning: null }
  }
  return chords
}

let overlay: OverlayView = copy(DEV_OVERLAY_VIEW)

const nothingSent = (what: string) => {
  console.info(`[dev shell] ${what}: nothing was sent to a client`)
  return null
}

const recording = <K extends keyof ShellCommands>(command: K) =>
  ((...args: unknown[]) => (devRecordingCall as (command: string, ...args: unknown[]) => unknown)(command, ...args)) as Handlers[K]

export const devCommands: Handlers = {
  currentState: async () => {
    const { dev, ready } = scenarios()
    await ready
    return copy(dev.state.value)
  },
  currentGame: async () => {
    const { dev, ready } = scenarios()
    await ready
    return copy(dev.game.value)
  },

  apiGet: async (path, query) => {
    if (path === '/truemains/lookup') return { players: devTruemainMarks() }
    return await readThroughProxy('GET', path, query)
  },
  apiPost: async (path, query, request) => {
    const { dev, ready } = scenarios()
    await ready
    if (/^\/champions\/\d+\/next-item$/.test(path) && dev.nextItem.value) return copy(dev.nextItem.value)
    try {
      return await readThroughProxy('POST', path, query, request)
    }
    catch (error) {
      // Offline: a scenario may carry the answer the API gave.
      if (path === '/champions/draft' && dev.recommendation.value) return copy(dev.recommendation.value)
      throw error
    }
  },
  openOnSite: (path) => {
    window.open(`${import.meta.env.TRUEMAIN_SITE_URL}${path}`, '_blank', 'noopener')
    return null
  },
  openChannel: (url) => {
    window.open(url, '_blank', 'noopener')
    return null
  },

  playerRecord: async () => {
    const { dev, ready } = scenarios()
    await ready
    return copy(dev.record.value) as PlayerRecord
  },
  playerHistory: () => [],
  playerGame: async (gameId) => {
    const fixtures = await import('~/fixtures/match-details.json')
    const detail = (fixtures.default as unknown as Record<string, GameDetail>)[String(gameId)]
    if (!detail) throw `no game ${gameId} in the dev fixtures`
    // The shared model's fixture game stands in for every game's timeline, so the curve shows in dev.
    const { default: winProbability } = await import('#shared/fixtures/win-probability-timeline.json')
    return { ...detail, winProbabilityTimeline: winProbability.timeline } as GameDetail
  },

  recordingSettings: recording('recordingSettings'),
  setRecordingSettings: recording('setRecordingSettings'),
  recordingCheckMarkKey: recording('recordingCheckMarkKey'),
  recordingStatus: recording('recordingStatus'),
  requestCapturePermission: recording('requestCapturePermission'),
  recordingLibrary: recording('recordingLibrary'),
  recordingGet: recording('recordingGet'),
  recordingWinProbability: recording('recordingWinProbability'),
  recordingSetKept: recording('recordingSetKept'),
  recordingDelete: recording('recordingDelete'),
  clipSave: recording('clipSave'),
  clipUpdate: recording('clipUpdate'),
  clipDelete: recording('clipDelete'),
  revealRecordingFile: recording('revealRecordingFile'),
  openYoutubeUpload: recording('openYoutubeUpload'),
  clipOpenYoutube: recording('clipOpenYoutube'),
  clipSetYoutubeUrl: recording('clipSetYoutubeUrl'),
  clipStartDrag: recording('clipStartDrag'),
  clipDeleteVideo: recording('clipDeleteVideo'),
  clipYoutubeEmbed: recording('clipYoutubeEmbed'),

  overlayView: () => copy(overlay),
  setOverlaySettings: (settings) => {
    overlay = { ...overlay, settings: settings as OverlaySettings, chords: devChords(settings as OverlaySettings) }
    return copy(overlay)
  },
  overlayCheckTrigger: (_, trigger) => {
    const chord = chordOfTrigger(trigger as Parameters<typeof chordOfTrigger>[0])
    return { refusal: null, chord: chord ? { label: chordLabel(chord, true), warning: null } : null }
  },
  overlayReadBinds: () => copy(overlay),
  overlayPreview: (on) => {
    overlay = { ...overlay, preview: on }
    return copy(overlay)
  },
  overlayFit: () => {},
  overlayConfirmation: () => 'Last 30 s saved',

  loadingPlayers: () => devLoadingView(),

  importRunes: () => nothingSent('rune page'),
  importItemSet: () => nothingSent('item set'),
  importKitItemSet: () => nothingSent('item set'),
  kitsGet: () => {
    try {
      return readStore(JSON.parse(localStorage.getItem(KITS_KEY) ?? 'null'))
    }
    catch {
      return { ...EMPTY_STORE }
    }
  },
  kitsSet: (store) => {
    const kept = readStore(store) as KitStore
    localStorage.setItem(KITS_KEY, JSON.stringify(kept))
    return kept
  },
  champSelectHover: () => nothingSent('hover'),
  champSelectLock: () => nothingSent('lock'),
  champSelectChoices: () => ({ writable: true, pickable: null, bannable: null }),
  champSelectSpells: () => nothingSent('summoner spells'),
  telemetryPage: () => {},
}

/** The shell's events: none arrives in dev — a scenario sets its state directly (`useDevScenarios`). */
export const devEvents = new Proxy({}, {
  get: () => ({ listen: async () => () => {} }),
}) as ShellEvents
