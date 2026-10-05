import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ref, watch, type Ref } from 'vue'

/**
 * `useAppUpdate` against stand-ins for the Nuxt auto-imports and the Tauri
 * plugins: the suite has no Nuxt runtime and no shell. Each test loads the
 * composable afresh, since it keeps the downloaded build at module level.
 */

const updater = vi.hoisted(() => ({ check: vi.fn() }))
const proc = vi.hoisted(() => ({ relaunch: vi.fn() }))
vi.mock('@tauri-apps/plugin-updater', () => updater)
vi.mock('@tauri-apps/plugin-process', () => proc)
const shellEvents = vi.hoisted(() => new Map<string, () => void>())
vi.mock('@tauri-apps/api/event', () => ({
  listen: vi.fn(async (name: string, handler: () => void) => {
    shellEvents.set(name, handler)
    return () => {}
  }),
}))

interface ToastMessage { id?: string, title: string, actions?: { label: string, onClick: () => void }[] }

let toasts: ToastMessage[]
let states: Map<string, Ref<unknown>>
let screen: Ref<string>

function fakeUpdate(install: () => Promise<void>) {
  return { version: '0.2.1', download: vi.fn(async () => {}), install: vi.fn(install) }
}

/** The update toast as it stands. */
function current() {
  return toasts.findLast(toast => toast.id === 'app-update')
}

/** Starts the composable the way `app.vue` does, with `update` on the feed. */
async function launch(update: ReturnType<typeof fakeUpdate>) {
  updater.check.mockResolvedValue(update)
  const { useAppUpdate } = await import('~/composables/useAppUpdate')
  const app = useAppUpdate()
  await app.start()
  await vi.advanceTimersByTimeAsync(0)
  return app
}

beforeEach(() => {
  vi.useFakeTimers()
  vi.resetModules()
  toasts = []
  states = new Map()
  screen = ref('lobby')
  proc.relaunch.mockReset().mockReturnValue(new Promise(() => {}))
  shellEvents.clear()
  vi.stubGlobal('useToast', () => ({
    add: (toast: ToastMessage) => toasts.push(toast),
    remove: (id: string) => (toasts = toasts.filter(toast => toast.id !== id)),
  }))
  vi.stubGlobal('useState', (key: string, init: () => unknown) => {
    if (!states.has(key)) states.set(key, ref(init()))
    return states.get(key)
  })
  vi.stubGlobal('useLcuState', () => ({ screen, ready: ref(true) }))
  vi.stubGlobal('watch', watch)
  vi.stubGlobal('onScopeDispose', () => {})
  vi.stubGlobal('insideTauri', () => true)
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('useAppUpdate install deadline', () => {
  it('offers a plain restart once an install outlives its deadline, without installing again', async () => {
    const update = fakeUpdate(() => new Promise(() => {}))
    const app = await launch(update)
    expect(current()?.title).toBe('Updating TrueMain to 0.2.1')

    await vi.advanceTimersByTimeAsync(29_000)
    expect(app.stalled.value).toBe(false)

    await vi.advanceTimersByTimeAsync(1_000)
    expect(app.stalled.value).toBe(true)
    expect(app.installing.value).toBe(true)
    const restart = current()?.actions?.find(action => action.label === 'Restart now')
    expect(restart).toBeDefined()

    restart!.onClick()
    await vi.advanceTimersByTimeAsync(0)
    expect(proc.relaunch).toHaveBeenCalledTimes(1)
    expect(update.install).toHaveBeenCalledTimes(1)
  })

  it('restarts from the sidebar during a stall instead of starting a second install', async () => {
    screen.value = 'draft'
    const update = fakeUpdate(() => new Promise(() => {}))
    const app = await launch(update)
    expect(app.readyVersion.value).toBe('0.2.1')

    void app.restart()
    await vi.advanceTimersByTimeAsync(30_000)
    expect(app.stalled.value).toBe(true)

    void app.restart()
    await vi.advanceTimersByTimeAsync(0)
    expect(update.install).toHaveBeenCalledTimes(1)
    expect(proc.relaunch).toHaveBeenCalledTimes(1)
  })

  it('leaves a healthy install alone before its deadline', async () => {
    const update = fakeUpdate(async () => {})
    const app = await launch(update)
    expect(proc.relaunch).toHaveBeenCalledTimes(1)

    await vi.advanceTimersByTimeAsync(29_000)
    expect(app.stalled.value).toBe(false)
    expect(current()?.title).toBe('Updating TrueMain to 0.2.1')
  })

  it('drops the deadline when the install fails', async () => {
    const update = fakeUpdate(async () => {
      throw new Error('signature mismatch')
    })
    const app = await launch(update)
    expect(app.installing.value).toBe(false)

    await vi.advanceTimersByTimeAsync(60_000)
    expect(app.stalled.value).toBe(false)
    expect(toasts.at(-1)?.title).toBe('The update could not be installed')
  })

  it('says so when the restart itself fails', async () => {
    proc.relaunch.mockRejectedValue(new Error('no process plugin'))
    const app = await launch(fakeUpdate(() => new Promise(() => {})))
    await vi.advanceTimersByTimeAsync(30_000)

    await app.restart()
    expect(current()?.title).toBe('TrueMain could not restart')
    expect(app.installing.value).toBe(true)
  })
})

describe('useAppUpdate during champion select and games', () => {
  it('offers a build found during champion select in the sidebar only, and in a toast once it is over', async () => {
    screen.value = 'draft'
    const update = fakeUpdate(() => new Promise(() => {}))
    const app = await launch(update)
    expect(app.readyVersion.value).toBe('0.2.1')
    expect(current()).toBeUndefined()

    screen.value = 'in-game'
    await vi.advanceTimersByTimeAsync(0)
    expect(current()).toBeUndefined()

    screen.value = 'home'
    await vi.advanceTimersByTimeAsync(0)
    expect(current()?.title).toBe('TrueMain 0.2.1 is ready')
    expect(update.install).not.toHaveBeenCalled()
  })

  it('takes an offer already on screen away for the phase and brings it back after', async () => {
    const update = fakeUpdate(() => new Promise(() => {}))
    updater.check.mockResolvedValue(null)
    const { useAppUpdate } = await import('~/composables/useAppUpdate')
    const app = useAppUpdate()
    await app.start()
    await vi.advanceTimersByTimeAsync(0)

    // Found by the timer, out of any phase.
    updater.check.mockResolvedValue(update)
    await vi.advanceTimersByTimeAsync(15 * 60 * 1000)
    expect(current()?.title).toBe('TrueMain 0.2.1 is ready')

    screen.value = 'draft'
    await vi.advanceTimersByTimeAsync(0)
    expect(current()).toBeUndefined()
    expect(app.readyVersion.value).toBe('0.2.1')

    screen.value = 'home'
    await vi.advanceTimersByTimeAsync(0)
    expect(current()?.title).toBe('TrueMain 0.2.1 is ready')
  })

  it('answers "Check for Updates…" at once, even during champion select', async () => {
    screen.value = 'draft'
    await launch(fakeUpdate(() => new Promise(() => {})))
    expect(current()).toBeUndefined()

    shellEvents.get('app://check-for-updates')!()
    await vi.advanceTimersByTimeAsync(0)
    expect(current()?.title).toBe('TrueMain 0.2.1 is ready')
  })
})
