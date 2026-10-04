import { describe, expect, it, vi } from 'vitest'
import { effectScope, nextTick, ref, shallowRef } from 'vue'
import { useRefetchFallback, type FetchStatus } from '#common/composables/useRefetchFallback'

/**
 * #1668: a failed filter or pager refetch used to wipe the rows on screen,
 * because `useAsyncData` resets `data` to its default when a request throws.
 * The source below is driven the way Nuxt drives it: `data`, `error` and
 * `status` are written together on settle, and a failure resets `data`.
 */
interface Page { rows: string[] }

const EMPTY: Page = { rows: [] }

function harness(scope?: () => string) {
  const data = shallowRef<Page>(EMPTY)
  const status = ref<FetchStatus>('idle')
  const error = shallowRef<unknown>(undefined)
  const onStaleFailure = vi.fn()

  const scopeHandle = effectScope()
  const fallback = scopeHandle.run(() =>
    useRefetchFallback({ data, status, error }, { scope, onStaleFailure }))!

  return {
    fallback,
    onStaleFailure,
    pending() {
      status.value = 'pending'
    },
    succeed(page: Page) {
      data.value = page
      error.value = undefined
      status.value = 'success'
    },
    fail(failure: unknown = new Error('500')) {
      error.value = failure
      data.value = EMPTY
      status.value = 'error'
    },
  }
}

describe('useRefetchFallback', () => {
  it('keeps the previous rows when a refetch fails, and reports it once as a stale failure', async () => {
    const source = harness()
    source.succeed({ rows: ['Ahri', 'Zed'] })
    await nextTick()

    source.pending()
    const failure = new Error('500')
    source.fail(failure)
    await nextTick()

    expect(source.fallback.data.value.rows).toEqual(['Ahri', 'Zed'])
    expect(source.fallback.error.value).toBeNull()
    expect(source.fallback.staleError.value).toBe(failure)
    expect(source.onStaleFailure).toHaveBeenCalledTimes(1)
    expect(source.onStaleFailure).toHaveBeenCalledWith(failure)
  })

  it('leaves a failed first load to the inline alert, without a toast', async () => {
    const source = harness()
    const failure = new Error('500')
    source.pending()
    source.fail(failure)
    await nextTick()

    expect(source.fallback.data.value).toBe(EMPTY)
    expect(source.fallback.error.value).toBe(failure)
    expect(source.fallback.staleError.value).toBeNull()
    expect(source.onStaleFailure).not.toHaveBeenCalled()
  })

  it('draws the last good rows while the request after a failure is in flight', async () => {
    const source = harness()
    source.succeed({ rows: ['Ahri'] })
    await nextTick()
    source.fail()
    await nextTick()

    // The next filter's entry is seeded from the failed one: the reset default.
    source.pending()
    await nextTick()

    expect(source.fallback.data.value.rows).toEqual(['Ahri'])
  })

  it('clears the stale state once a refetch succeeds', async () => {
    const source = harness()
    source.succeed({ rows: ['Ahri'] })
    await nextTick()
    source.fail()
    await nextTick()
    source.succeed({ rows: ['Lux'] })
    await nextTick()

    expect(source.fallback.data.value.rows).toEqual(['Lux'])
    expect(source.fallback.staleError.value).toBeNull()
    expect(source.fallback.error.value).toBeNull()
  })

  it('does not toast the same failure twice', async () => {
    const source = harness()
    source.succeed({ rows: ['Ahri'] })
    await nextTick()
    const failure = new Error('500')
    source.fail(failure)
    await nextTick()
    // A key flip back onto the entry still holding that error.
    source.pending()
    source.fail(failure)
    await nextTick()

    expect(source.onStaleFailure).toHaveBeenCalledTimes(1)
  })

  it('never substitutes a payload fetched under another scope', async () => {
    const scope = ref('ahri')
    const source = harness(() => scope.value)
    source.succeed({ rows: ['Ahri build'] })
    await nextTick()

    scope.value = 'zed'
    source.pending()
    source.fail()
    await nextTick()

    expect(source.fallback.data.value).toBe(EMPTY)
    expect(source.fallback.error.value).not.toBeNull()
    expect(source.onStaleFailure).not.toHaveBeenCalled()
  })

  it('does not resurrect a payload once an empty answer settled after it', async () => {
    const data = shallowRef<Page | null>(null)
    const status = ref<FetchStatus>('idle')
    const error = shallowRef<unknown>(undefined)
    const fallback = effectScope().run(() => useRefetchFallback({ data, status, error }))!

    data.value = { rows: ['Ahri'] }
    status.value = 'success'
    await nextTick()
    data.value = null
    await nextTick()
    error.value = new Error('500')
    status.value = 'error'
    await nextTick()

    expect(fallback.data.value).toBeNull()
    expect(fallback.error.value).not.toBeNull()
  })
})
