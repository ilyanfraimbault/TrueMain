import { describe, expect, it, vi } from 'vitest'
import * as vue from 'vue'

/**
 * `useTruemainMatches` refires on `page`, `position` and `championId`, and
 * `$fetch` resolutions are not ordered: on a slow link, stepping from page 3 to
 * page 4 could let page 3's response resolve last and write its rows while the
 * pager already reads 4 — the list and the control disagreeing, with nothing on
 * screen saying which is stale.
 *
 * The invariant is the one `useCompositionBuild` and `useTruemainSearch` state
 * in their own words: only the newest request may write the refs. That covers
 * profile, rank history, activity and matches at once, since they all run
 * through this composable.
 *
 * Response order is driven by hand-resolved promises rather than timers, so
 * "the older request finishes last" is an assertion about ordering and not
 * about how loaded the CI box happens to be.
 *
 * Same auto-import stand-in as `client-only.test.ts` in this folder.
 */
Object.assign(globalThis, {
  ref: vue.ref,
  computed: vue.computed,
  watch: vue.watch,
  toValue: vue.toValue,
  onMounted: vue.onMounted,
})

const { useTruemainFetch } = await import('~/composables/useTruemainFetch')

interface Payload { page: number }

interface Deferred {
  promise: Promise<Payload>
  resolve: (value: Payload) => void
  reject: (reason: unknown) => void
}

function deferred(): Deferred {
  let resolve!: (value: Payload) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<Payload>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

function harness(pending: Deferred[], page: vue.Ref<number>, nameTag: vue.Ref<string>) {
  const data = vue.ref<Payload | null>(null)
  const calls: string[] = []
  let loading!: vue.Ref<boolean>
  let notFound!: vue.Ref<boolean>
  let error!: vue.Ref<unknown>

  const request = vi.fn((tag: string) => {
    calls.push(tag)
    const next = pending.shift()
    if (!next) throw new Error('unexpected extra request')
    return next.promise
  })

  const component = vue.defineComponent({
    setup() {
      const bundle = useTruemainFetch<Payload>(nameTag, {
        request,
        watch: [page],
        validate: (response): response is Payload => Boolean(response),
        onResponse: (response) => { data.value = response },
        onClear: () => { data.value = null },
      })
      loading = bundle.isLoading
      notFound = bundle.notFound
      error = bundle.error
      return () => vue.h('div')
    },
  })

  vue.createApp(component).mount(document.createElement('div'))
  return { data, loading, notFound, error, request, calls }
}

/** Drain the microtask queue so a just-settled promise's handlers have run. */
const drain = () => new Promise(resolve => setTimeout(resolve, 0))

describe('useTruemainFetch out-of-order responses', () => {
  it('keeps the newest response when an older one resolves last', async () => {
    const page3 = deferred()
    const page4 = deferred()
    const page = vue.ref(3)
    const { data, loading, request } = harness([page3, page4], page, vue.ref('Sheiden-1234'))

    await vue.nextTick()
    page.value = 4
    await vue.nextTick()
    expect(request).toHaveBeenCalledTimes(2)

    // Page 4 comes back first, then the superseded page 3.
    page4.resolve({ page: 4 })
    await drain()
    page3.resolve({ page: 3 })
    await drain()

    // Without the token guard this reads `{ page: 3 }` under a pager showing 4.
    expect(data.value).toEqual({ page: 4 })
    expect(loading.value).toBe(false)
  })

  it('does not let a superseded failure raise an error over a live response', async () => {
    const stale = deferred()
    const fresh = deferred()
    const page = vue.ref(1)
    const { data, error, loading } = harness([stale, fresh], page, vue.ref('Sheiden-1234'))

    await vue.nextTick()
    page.value = 2
    await vue.nextTick()

    fresh.resolve({ page: 2 })
    await drain()
    stale.reject(new Error('stale failure'))
    await drain()

    expect(data.value).toEqual({ page: 2 })
    expect(error.value).toBeNull()
    expect(loading.value).toBe(false)
  })

  it('does not resurrect an in-flight response after the name tag empties', async () => {
    const inFlight = deferred()
    const nameTag = vue.ref('Sheiden-1234')
    const { data, notFound } = harness([inFlight], vue.ref(1), nameTag)

    await vue.nextTick()
    // An empty name tag clears synchronously — no second request is issued.
    nameTag.value = ''
    await vue.nextTick()

    inFlight.resolve({ page: 1 })
    await drain()

    // The cleared state is the current answer; the response that was already in
    // flight must not put the previous player's payload back on screen.
    expect(data.value).toBeNull()
    expect(notFound.value).toBe(false)
  })
})
