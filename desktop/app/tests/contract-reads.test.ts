import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, nextTick, ref, watch, type Ref } from 'vue'
import type { DraftState } from '~/types/lcu'

/**
 * The app's reads of the site's endpoints, against stand-ins for the Nuxt auto-imports:
 * a champion's builds from the site's page endpoint (through the shell when it runs, the
 * dev proxy otherwise), a true main's own build with its across-lanes fallback, and the
 * draft's two requests through the shell.
 */

const invoke = vi.hoisted(() => vi.fn())
vi.mock('@tauri-apps/api/core', () => ({ invoke }))

let states: Map<string, Ref<unknown>>
let inShell: boolean
const fetcher = vi.fn()
const apiGet = vi.fn()

beforeEach(() => {
  vi.resetModules()
  vi.useFakeTimers()
  states = new Map()
  inShell = false
  invoke.mockReset()
  fetcher.mockReset()
  apiGet.mockReset()
  vi.stubGlobal('useState', (key: string, init: () => unknown) => {
    if (!states.has(key)) states.set(key, ref(init()))
    return states.get(key)
  })
  vi.stubGlobal('ref', ref)
  vi.stubGlobal('computed', computed)
  vi.stubGlobal('watch', watch)
  vi.stubGlobal('onScopeDispose', () => {})
  vi.stubGlobal('insideTauri', () => inShell)
  vi.stubGlobal('trackLoad', (work: Promise<unknown>) => work)
  vi.stubGlobal('$fetch', fetcher)
  vi.stubGlobal('apiGet', apiGet)
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

const builds = { championId: 7, builds: [{ firstItemId: 3078 }] }

describe('useChampionBuilds', () => {
  it('reads a lane\'s builds from the site\'s champion endpoint, once per session', async () => {
    invoke.mockResolvedValue(builds)
    const { useChampionBuilds } = await import('~/composables/useChampionBuilds')

    const first = useChampionBuilds(ref(7), ref('TOP'))
    await vi.waitFor(() => expect(first.response.value).toEqual(builds))
    useChampionBuilds(ref(7), ref('TOP'))
    await nextTick()

    expect(invoke).toHaveBeenCalledTimes(1)
    expect(invoke).toHaveBeenCalledWith('api_get', { path: '/champions/7', query: [['position', 'TOP']] })
  })

  it('asks the shell when it runs', async () => {
    inShell = true
    invoke.mockResolvedValue(builds)
    const { useChampionBuilds } = await import('~/composables/useChampionBuilds')

    const answer = useChampionBuilds(ref(7), ref('JUNGLE'))
    await vi.waitFor(() => expect(answer.response.value).toEqual(builds))

    expect(invoke).toHaveBeenCalledWith('api_get', { path: '/champions/7', query: [['position', 'JUNGLE']] })
  })
})

describe('useTruemainBuild', () => {
  it('asks across lanes when the main never played the champion on this one', async () => {
    apiGet.mockImplementation(async (_: string, query: { position: string | null }) => {
      if (query.position) throw Object.assign(new Error('404'), { statusCode: 404 })
      return builds
    })
    const { useTruemainBuild } = await import('~/composables/useTruemainBuild')
    const truemain = useTruemainBuild()

    await truemain.load('Faker-KR1', 7, 'MIDDLE')

    expect(apiGet).toHaveBeenNthCalledWith(1, '/truemains/Faker-KR1/champions/7', { position: 'MIDDLE' })
    expect(apiGet).toHaveBeenNthCalledWith(2, '/truemains/Faker-KR1/champions/7', { position: null })
    expect(truemain.answerOf('Faker-KR1', 7, 'MIDDLE')).toEqual(builds)
  })
})

describe('the draft requests through the shell', () => {
  const draft = (): DraftState => ({
    myPosition: 'MIDDLE',
    myChampion: null,
    myChampionLocked: false,
    allyChampions: [],
    enemyChampions: [],
    myTeam: [],
    allyBans: [],
    enemyBans: [],
    secondsLeft: 30,
    timerPhase: 'BAN_PICK',
    myAction: null,
    myNextAction: null,
    turnsUntilMyAction: null,
  })

  it('ask the shell for the picks and the bans', async () => {
    inShell = true
    invoke.mockImplementation(async (_: string, args: { path: string }) =>
      args.path === '/champions/draft/bans' ? { candidates: [] } : { enemyLanes: [], candidates: [] })
    const { useDraftRecommendation } = await import('~/composables/useDraftRecommendation')
    const { useDraftBans } = await import('~/composables/useDraftBans')

    useDraftRecommendation(ref(draft()), ref({}), ref([103]))
    useDraftBans(ref(draft()), ref([103]), ref(true))
    await vi.advanceTimersByTimeAsync(200)

    const post = (path: string) => ({ path, query: [], request: expect.objectContaining({ position: 'MIDDLE' }), replaces: null })
    expect(invoke).toHaveBeenCalledWith('api_post', post('/champions/draft'))
    expect(invoke).toHaveBeenCalledWith('api_post', post('/champions/draft/bans'))
  })
})
