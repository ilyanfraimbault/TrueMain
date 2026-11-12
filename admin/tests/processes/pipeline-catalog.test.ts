import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ref, type Ref } from 'vue'
import { CATALOG } from '../pipeline-chain/catalog'

/**
 * `usePipelineCatalog` against stand-ins for the Nuxt auto-imports: the catalog is asked
 * once per session however many components draw a step, and asked again after a failure.
 */

let states: Map<string, Ref<unknown>>
const opsCall = vi.fn()

beforeEach(() => {
  vi.resetModules()
  states = new Map()
  opsCall.mockReset()
  vi.stubGlobal('useState', (key: string, init: () => unknown) => {
    if (!states.has(key)) states.set(key, ref(init()))
    return states.get(key)
  })
  vi.stubGlobal('opsCall', opsCall)
})

describe('usePipelineCatalog', () => {
  it('reads the catalog once, for every component that asks', async () => {
    opsCall.mockResolvedValue(CATALOG)
    const { usePipelineCatalog } = await import('~/composables/usePipelineCatalog')

    const first = usePipelineCatalog()
    const second = usePipelineCatalog()
    await vi.waitFor(() => expect(first.value).toEqual(CATALOG))

    expect(second.value).toEqual(CATALOG)
    usePipelineCatalog()
    expect(opsCall).toHaveBeenCalledTimes(1)
    expect(opsCall).toHaveBeenCalledWith('get', '/pipeline')
  })

  it('stays empty when the read fails, and the next component asks again', async () => {
    opsCall.mockRejectedValueOnce(new Error('502')).mockResolvedValueOnce(CATALOG)
    const { usePipelineCatalog } = await import('~/composables/usePipelineCatalog')

    const first = usePipelineCatalog()
    await vi.waitFor(() => expect(opsCall).toHaveBeenCalledTimes(1))
    await Promise.resolve()
    expect(first.value).toBeNull()

    await vi.waitFor(() => {
      usePipelineCatalog()
      expect(first.value).toEqual(CATALOG)
    })
    expect(opsCall).toHaveBeenCalledTimes(2)
  })
})
