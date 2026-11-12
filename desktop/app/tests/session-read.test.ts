import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref, type Ref } from 'vue'

const ipc = vi.hoisted(() => ({ invoke: vi.fn() }))
vi.mock('@tauri-apps/api/core', () => ({ invoke: ipc.invoke }))

let states: Map<string, Ref<unknown>>

beforeEach(() => {
  vi.resetModules()
  vi.useFakeTimers()
  states = new Map()
  ipc.invoke.mockReset()
  vi.stubGlobal('ref', ref)
  vi.stubGlobal('computed', computed)
  vi.stubGlobal('useState', (key: string, init: () => unknown) => {
    if (!states.has(key)) states.set(key, ref(init()))
    return states.get(key)
  })
  vi.stubGlobal('trackLoad', (work: Promise<unknown>) => work)
  vi.stubGlobal('insideTauri', () => true)
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('useSessionRead', () => {
  it('retries a failed read on its own, waiting twice as long each time, until it lands', async () => {
    const read = vi.fn()
      .mockRejectedValueOnce(new Error('cold'))
      .mockRejectedValueOnce(new Error('cold'))
      .mockResolvedValue('tiers')
    const { useSessionRead } = await import('~/composables/useSessionRead')
    const { data, status } = useSessionRead('test-read', read)

    await vi.advanceTimersByTimeAsync(0)
    expect(status.value).toBe('error')
    await vi.advanceTimersByTimeAsync(1_999)
    expect(read).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(1)
    expect(read).toHaveBeenCalledTimes(2)
    await vi.advanceTimersByTimeAsync(4_000)
    expect(read).toHaveBeenCalledTimes(3)
    expect(status.value).toBe('ready')
    expect(data.value).toBe('tiers')
  })

  it('reads once per session, however many callers ask', async () => {
    const read = vi.fn().mockResolvedValue('profiles')
    const { useSessionRead } = await import('~/composables/useSessionRead')
    useSessionRead('shared-read', read)
    await vi.advanceTimersByTimeAsync(0)
    useSessionRead('shared-read', read)
    await vi.advanceTimersByTimeAsync(0)
    expect(read).toHaveBeenCalledTimes(1)
  })
})

describe('the session reads', () => {
  it('read the tier list through the shell, as entries by lane', async () => {
    ipc.invoke.mockResolvedValue({ patchVersion: '26.21', position: null, tiers: [{ tier: 'S', entries: [{ championId: 103, position: 'MIDDLE', games: 10, winRate: 0.55, pickRate: 0.1, banRate: 0.05 }] }] })
    const { useTierList } = await import('~/composables/useTierList')
    const tiers = useTierList()
    await vi.advanceTimersByTimeAsync(0)
    expect(ipc.invoke).toHaveBeenCalledWith('api_get', { path: '/champions/tierlist', query: [] })
    expect(tiers.patch.value).toBe('26.21')
    expect(tiers.entryOf(103, 'MIDDLE')?.tier).toBe('S')
  })

  it('read the damage profiles through the shell', async () => {
    ipc.invoke.mockResolvedValue({ patch: '26.21', profiles: [] })
    const { useDamageProfiles } = await import('~/composables/useDamageProfiles')
    const damage = useDamageProfiles()
    await vi.advanceTimersByTimeAsync(0)
    expect(ipc.invoke).toHaveBeenCalledWith('api_get', { path: '/champions/damage-profiles', query: [] })
    expect(damage.patch.value).toBe('26.21')
    expect(damage.profileOf(103, 'MIDDLE')).toBeNull()
  })
})
