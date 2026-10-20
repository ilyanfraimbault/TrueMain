import { flushPromises } from '@vue/test-utils'
import { mockNuxtImport, mountSuspended } from '@nuxt/test-utils/runtime'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import type { ApexCutoff } from '~~/shared/types/apex-cutoffs'
import { useApexCutoffs } from '#common/composables/useApexCutoffs'

// The ranked card's apex cutoffs (#2174) and the loading state that keeps its
// skeleton until they answer (#2187).

const { apiFetch } = vi.hoisted(() => ({ apiFetch: vi.fn() }))
mockNuxtImport('useApiFetch', () => () => apiFetch)

afterEach(() => apiFetch.mockReset())

const ENTRY: ApexCutoff = { dateUtc: '2026-10-20', grandmasterMinLp: 1594, challengerMinLp: 2251 }

async function mountWith(platform: { value: string | null }) {
  let state!: ReturnType<typeof useApexCutoffs>
  await mountSuspended(defineComponent({
    setup() {
      state = useApexCutoffs(() => platform.value)
      return () => h('div')
    },
  }))
  return state
}

describe('useApexCutoffs', () => {
  it('loads the platform\'s cutoffs, then stops loading', async () => {
    let answer!: (value: unknown) => void
    apiFetch.mockReturnValue(new Promise((resolve) => { answer = resolve }))

    const state = await mountWith(ref('EUW1'))
    expect(state.loading.value).toBe(true)
    expect(apiFetch).toHaveBeenCalledWith('/ladder/EUW1/apex-cutoffs', expect.objectContaining({ query: { days: 90 } }))

    answer({ entries: [ENTRY] })
    await flushPromises()
    expect(state.loading.value).toBe(false)
    expect(state.cutoffs.value).toEqual([ENTRY])
  })

  it('reads a failed request as no cutoff, and stops loading', async () => {
    apiFetch.mockRejectedValue(new Error('down'))

    const state = await mountWith(ref('KR'))
    await flushPromises()

    expect(state.loading.value).toBe(false)
    expect(state.cutoffs.value).toEqual([])
  })

  it('waits for nothing without a platform', async () => {
    const state = await mountWith(ref(null))
    await flushPromises()

    expect(state.loading.value).toBe(false)
    expect(state.cutoffs.value).toEqual([])
    expect(apiFetch).not.toHaveBeenCalled()
  })
})
