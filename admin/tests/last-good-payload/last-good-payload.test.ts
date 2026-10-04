import { useLastGoodPayload } from '~/composables/useLastGoodPayload'
import { describe, expect, it } from 'vitest'
import { nextTick, ref } from 'vue'

// Nuxt resets a `useFetch` result's `data` to its default when a fetch fails, so a
// panel that drew `data ?? 0` rendered a fabricated quiet window (#1426). These pin
// what the panel draws instead: the remembered payload for the same key, or nothing.

interface Payload { totalCalls: number }

describe('useLastGoodPayload', () => {
  it('returns the current payload when there is one', () => {
    const data = ref<Payload | undefined>({ totalCalls: 130_000 })
    const usage = useLastGoodPayload(data, () => '24h')
    expect(usage.value).toEqual({ totalCalls: 130_000 })
  })

  it('returns null, not a zeroed payload, when the first fetch fails', () => {
    const data = ref<Payload | undefined>(undefined)
    expect(useLastGoodPayload(data, () => '24h').value).toBeNull()
  })

  it('keeps the previous payload when a re-fetch of the same key fails', async () => {
    const data = ref<Payload | undefined>({ totalCalls: 130_000 })
    const usage = useLastGoodPayload(data, () => '24h')
    data.value = undefined
    await nextTick()
    expect(usage.value).toEqual({ totalCalls: 130_000 })
  })

  it('never substitutes a payload fetched for another key', async () => {
    const key = ref('24h')
    const data = ref<Payload | undefined>({ totalCalls: 130_000 })
    const usage = useLastGoodPayload(data, key)
    key.value = '1h'
    data.value = undefined
    await nextTick()
    expect(usage.value).toBeNull()
  })

  it('remembers the newest successful payload', async () => {
    const data = ref<Payload | undefined>({ totalCalls: 1 })
    const usage = useLastGoodPayload(data, () => '24h')
    data.value = { totalCalls: 2 }
    await nextTick()
    data.value = undefined
    await nextTick()
    expect(usage.value).toEqual({ totalCalls: 2 })
  })
})
