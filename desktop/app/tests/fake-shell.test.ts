import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref, type Ref } from 'vue'
import { later } from '~/utils/shell'

describe('later', () => {
  it('loads the object on the first call and forwards every call to it', async () => {
    const load = vi.fn(async () => ({ greet: (name: string) => `hello ${name}` }))
    const lazy = later(load)
    expect(load).not.toHaveBeenCalled()
    expect(await lazy.greet('Ahri')).toBe('hello Ahri')
    expect(await lazy.greet('Zed')).toBe('hello Zed')
  })
})

describe('the app on the dev server', () => {
  let states: Map<string, Ref<unknown>>

  beforeEach(() => {
    vi.resetModules()
    states = new Map()
    vi.stubGlobal('ref', ref)
    vi.stubGlobal('computed', computed)
    vi.stubGlobal('onMounted', (run: () => unknown) => void run())
    vi.stubGlobal('useState', (key: string, init: () => unknown) => {
      if (!states.has(key)) states.set(key, ref(init()))
      return states.get(key)
    })
    vi.stubGlobal('useToast', () => ({ add: () => {} }))
    vi.stubGlobal('useRouter', () => ({ currentRoute: ref({ path: '/' }), push: vi.fn() }))
  })

  afterEach(() => {
    vi.doUnmock('~/utils/shell')
    vi.unstubAllGlobals()
  })

  it('serves the recordings\' files from the dev server, and leaves a console hook for the recap', async () => {
    vi.doMock('~/utils/shell', async () => {
      const actual = await vi.importActual<typeof import('~/utils/shell')>('~/utils/shell')
      return { ...actual, fakeShell: true, shell: { recordingSettings: async () => null, recordingStatus: async () => null, recordingLibrary: async () => null }, shellEvents: { recordingStatus: { listen: async () => () => {} }, recordingLibrary: { listen: async () => () => {} }, recordingRecap: { listen: async () => () => {} } } }
    })
    const { useRecordings } = await import('~/composables/useRecordings')
    const recordings = useRecordings()
    await new Promise(resolve => setTimeout(resolve, 0))
    expect(recordings.fileSrc('/r/a.mp4')).toBe('/__dev/recording-file?path=%2Fr%2Fa.mp4')
    expect(typeof (window as unknown as Record<string, unknown>).__devRecordingRecap).toBe('function')
  })

  it('switches the state and the game to a scenario', async () => {
    const { useDevScenarios } = await import('~/composables/useDevScenarios')
    const dev = useDevScenarios()
    await dev.load()
    dev.select('in-game-early')
    expect(dev.state.value.screen).toBe('in-game')
    expect(dev.game.value).not.toBeNull()
    dev.select('nope')
    expect(dev.state.value.connected).toBe(false)
  })
})
