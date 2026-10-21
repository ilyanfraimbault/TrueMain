import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import OverlayConfirmation from '../app/pages/overlay/confirmation.vue'

// The page outside Nuxt and outside the shell: its auto-imports stubbed as
// globals, the shell's command and event answered by the test.
const invoke = vi.fn()
const unlisten = vi.fn()
let deliver: ((event: { payload: string }) => void) | undefined
vi.mock('@tauri-apps/api/core', () => ({ invoke: (...args: unknown[]) => invoke(...args) }))
vi.mock('@tauri-apps/api/event', () => ({
  listen: async (_event: string, handler: (event: { payload: string }) => void) => {
    deliver = handler
    return unlisten
  },
}))

const global = { stubs: { UIcon: true } }
let tauri = true

beforeEach(() => {
  for (const [name, value] of Object.entries({ ref, watch, nextTick, onMounted, onBeforeUnmount })) vi.stubGlobal(name, value)
  vi.stubGlobal('useHead', () => {})
  vi.stubGlobal('insideTauri', () => tauri)
  tauri = true
  deliver = undefined
  invoke.mockReset()
  unlisten.mockReset()
  invoke.mockImplementation(async (command: string) => (command === 'overlay_confirmation' ? 'Last 1 min saved' : undefined))
})

afterEach(() => vi.unstubAllGlobals())

describe('the overlay\'s confirmation of a kept mark', () => {
  it('shows the press that built its window and fits its window to it', async () => {
    const wrapper = mount(OverlayConfirmation, { global })
    await flushPromises()

    expect(wrapper.text()).toBe('Last 1 min saved')
    expect(invoke).toHaveBeenCalledWith('overlay_fit', expect.objectContaining({ panel: 'confirmation' }))
  })

  it('follows each new press, and stops listening when closed', async () => {
    const wrapper = mount(OverlayConfirmation, { global })
    await flushPromises()

    deliver!({ payload: 'Last 2 min saved' })
    await flushPromises()
    expect(wrapper.text()).toBe('Last 2 min saved')

    wrapper.unmount()
    expect(unlisten).toHaveBeenCalled()
  })

  it('shows nothing once its moment is over', async () => {
    invoke.mockImplementation(async () => null)
    const wrapper = mount(OverlayConfirmation, { global })
    await flushPromises()
    expect(wrapper.text()).toBe('')
  })

  it('shows a sample line in a browser', async () => {
    tauri = false
    const wrapper = mount(OverlayConfirmation, { global })
    await flushPromises()

    expect(wrapper.text()).toBe('Last 30 s saved')
    expect(invoke).not.toHaveBeenCalled()
  })
})
