import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { defineComponent, h, nextTick, ref } from 'vue'
import { useKeyedFetch } from '~/composables/useKeyedFetch'
import { failKeyed, keyedId, keyedValue, startKeyed } from '~/utils/keyed-fetch'

// One fetch for one entity (#2471): the entity travels with every phase, and only the
// latest request writes.

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (error: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

describe('keyed-fetch transitions', () => {
  it('keeps an answer only across a reload of the same id', () => {
    const ready = { s: 'ready' as const, id: 'a', value: 1 }
    expect(startKeyed(ready, 'a')).toEqual({ s: 'loading', id: 'a', previous: 1 })
    expect(startKeyed(ready, 'b')).toEqual({ s: 'loading', id: 'b', previous: null })
    expect(startKeyed({ s: 'idle' }, 'a')).toEqual({ s: 'loading', id: 'a', previous: null })
  })

  it('keeps the same-id answer through a failure, never another id\'s', () => {
    const reloading = { s: 'loading' as const, id: 'a', previous: 1 }
    expect(failKeyed(reloading, 'a', 'boom')).toEqual({ s: 'error', id: 'a', error: 'boom', previous: 1 })
    expect(failKeyed(reloading, 'b', 'boom')).toEqual({ s: 'error', id: 'b', error: 'boom', previous: null })
  })

  it('reads the value and the id of every phase', () => {
    expect(keyedValue({ s: 'idle' })).toBeNull()
    expect(keyedValue({ s: 'ready', id: 'a', value: 2 })).toBe(2)
    expect(keyedValue({ s: 'error', id: 'a', error: 'x', previous: 3 })).toBe(3)
    expect(keyedId({ s: 'idle' })).toBeNull()
    expect(keyedId({ s: 'loading', id: 'a', previous: null })).toBe('a')
  })

  it('compares composite ids with the given equality', () => {
    const same = (x: { id: string }, y: { id: string }) => x.id === y.id
    const ready = { s: 'ready' as const, id: { id: 'a' }, value: 1 }
    expect(startKeyed(ready, { id: 'a' }, same)).toEqual({ s: 'loading', id: { id: 'a' }, previous: 1 })
    expect(startKeyed(ready, { id: 'a' })).toEqual({ s: 'loading', id: { id: 'a' }, previous: null })
  })
})

describe('useKeyedFetch', () => {
  it('drops a slow answer for A once B was asked for, failed or not', async () => {
    const answers = { a: deferred<string>(), b: deferred<string>() }
    const { state, load } = useKeyedFetch<'a' | 'b', string>(id => answers[id].promise)
    void load('a')
    void load('b')
    answers.b.resolve('B')
    await flushPromises()
    expect(state.value).toEqual({ s: 'ready', id: 'b', value: 'B' })

    answers.a.resolve('A')
    await flushPromises()
    expect(state.value).toEqual({ s: 'ready', id: 'b', value: 'B' })
  })

  it('does not let A\'s late failure end B\'s loading', async () => {
    const answers = { a: deferred<string>(), b: deferred<string>() }
    const { state, load } = useKeyedFetch<'a' | 'b', string>(id => answers[id].promise)
    void load('a')
    void load('b')
    answers.a.reject(new Error('boom'))
    await flushPromises()
    expect(state.value).toEqual({ s: 'loading', id: 'b', previous: null })
  })

  it('keeps the answer on screen through a reload, and through its failure', async () => {
    const fetch = vi.fn().mockResolvedValueOnce('v1').mockRejectedValueOnce(new Error('down'))
    const { state, value, load, reload } = useKeyedFetch<string, string>(fetch)
    await load('a')
    const pending = reload()
    expect(state.value).toEqual({ s: 'loading', id: 'a', previous: 'v1' })
    expect(value.value).toBe('v1')
    await pending
    expect(state.value.s).toBe('error')
    expect(value.value).toBe('v1')
  })

  it('goes back to idle on reset, dropping what is in flight', async () => {
    const answer = deferred<string>()
    const { state, load, reset } = useKeyedFetch<string, string>(() => answer.promise)
    void load('a')
    reset()
    answer.resolve('late')
    await flushPromises()
    expect(state.value).toEqual({ s: 'idle' })
  })

  it('follows an id source, and idles on null', async () => {
    const id = ref<string | null>('a')
    const fetch = vi.fn(async (key: string) => key.toUpperCase())
    let fetched!: ReturnType<typeof useKeyedFetch<string, string>>
    mount(defineComponent({
      setup() {
        fetched = useKeyedFetch(fetch, { id })
        return () => h('div')
      },
    }))
    await flushPromises()
    expect(fetched.state.value).toEqual({ s: 'ready', id: 'a', value: 'A' })
    id.value = 'b'
    await flushPromises()
    expect(fetched.value.value).toBe('B')
    id.value = null
    await nextTick()
    expect(fetched.state.value).toEqual({ s: 'idle' })
  })

  it('writes nothing once its owner unmounted', async () => {
    const answer = deferred<string>()
    let fetched!: ReturnType<typeof useKeyedFetch<string, string>>
    const wrapper = mount(defineComponent({
      setup() {
        fetched = useKeyedFetch(() => answer.promise, { id: 'a' })
        return () => h('div')
      },
    }))
    wrapper.unmount()
    answer.resolve('late')
    await flushPromises()
    expect(fetched.state.value.s).toBe('loading')
  })
})
