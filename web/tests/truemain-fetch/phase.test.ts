import { describe, expect, it } from 'vitest'
import * as vue from 'vue'

/**
 * The fetch bundle is one phase now (#2476): these cover the combinations the
 * old four-flag bundle could hold and should not have — a server error read as
 * "not found", "loading" stuck on after the name tag emptied, a stale
 * not-found or error kept while the next request loads.
 *
 * Same auto-import stand-in as `client-only.test.ts` in this folder.
 */
Object.assign(globalThis, {
  ref: vue.ref,
  shallowRef: vue.shallowRef,
  computed: vue.computed,
  watch: vue.watch,
  toValue: vue.toValue,
  onMounted: vue.onMounted,
  onScopeDispose: vue.onScopeDispose,
})

const { useTruemainFetch } = await import('#common/composables/useTruemainFetch')

interface Payload { page: number }

function httpError(statusCode: number) {
  return Object.assign(new Error(`HTTP ${statusCode}`), { statusCode })
}

interface Pending {
  resolve: (value: Payload) => void
  reject: (reason: unknown) => void
}

function harness(nameTag: vue.Ref<string>, page: vue.Ref<number>) {
  const pending: Pending[] = []
  const cleared: number[] = []
  let bundle!: ReturnType<typeof useTruemainFetch<Payload>>

  const component = vue.defineComponent({
    setup() {
      bundle = useTruemainFetch<Payload>(nameTag, {
        request: () => new Promise<Payload>((resolve, reject) => pending.push({ resolve, reject })),
        watch: [page],
        validate: (response): response is Payload => Boolean(response),
        onResponse: () => {},
        onClear: () => { cleared.push(pending.length) },
      })
      return () => vue.h('div')
    },
  })

  vue.createApp(component).mount(document.createElement('div'))
  return { pending, cleared, bundle: () => bundle }
}

const settle = () => new Promise(resolve => setTimeout(resolve, 0))

describe('useTruemainFetch phase', () => {
  it('reads a server error as an error, never as not found', async () => {
    const { pending, bundle } = harness(vue.ref('Sheiden-1234'), vue.ref(1))
    await vue.nextTick()

    pending[0]!.reject(httpError(502))
    await settle()

    expect(bundle().notFound.value).toBe(false)
    expect(bundle().error.value).toEqual(httpError(502))
    expect(bundle().isLoading.value).toBe(false)
    expect(bundle().state.value.kind).toBe('error')
  })

  it('reads a 404 as not found and clears the consumer', async () => {
    const { pending, cleared, bundle } = harness(vue.ref('Nobody-0000'), vue.ref(1))
    await vue.nextTick()

    pending[0]!.reject(httpError(404))
    await settle()

    expect(bundle().notFound.value).toBe(true)
    expect(bundle().error.value).toBeNull()
    expect(cleared).toHaveLength(1)
  })

  it('stops loading when the name tag empties mid-request', async () => {
    const nameTag = vue.ref('Sheiden-1234')
    const { pending, bundle } = harness(nameTag, vue.ref(1))
    await vue.nextTick()
    expect(bundle().isLoading.value).toBe(true)

    nameTag.value = ''
    await vue.nextTick()
    pending[0]!.resolve({ page: 1 })
    await settle()

    expect(bundle().isLoading.value).toBe(false)
    expect(bundle().state.value.kind).toBe('cleared')
  })

  it('drops a previous error and not-found as soon as the next request starts', async () => {
    const page = vue.ref(1)
    const { pending, bundle } = harness(vue.ref('Sheiden-1234'), page)
    await vue.nextTick()
    pending[0]!.reject(httpError(500))
    await settle()
    expect(bundle().error.value).not.toBeNull()

    page.value = 2
    await vue.nextTick()
    expect(bundle().error.value).toBeNull()
    expect(bundle().isLoading.value).toBe(true)

    pending[1]!.resolve({ page: 2 })
    await settle()
    expect(bundle().state.value).toEqual({ kind: 'ready', data: { page: 2 } })
  })

  it('keeps the last answer through a page turn, so it is no cold load', async () => {
    const page = vue.ref(1)
    const { pending, bundle } = harness(vue.ref('Sheiden-1234'), page)
    await vue.nextTick()
    expect(bundle().isInitialLoading.value).toBe(true)
    pending[0]!.resolve({ page: 1 })
    await settle()

    page.value = 2
    await vue.nextTick()
    expect(bundle().isInitialLoading.value).toBe(false)
    expect(bundle().state.value).toEqual({ kind: 'loading', previous: { page: 1 } })

    // A failed page turn keeps page 1 on screen beside the error.
    pending[1]!.reject(httpError(503))
    await settle()
    expect(bundle().state.value).toMatchObject({ kind: 'error', previous: { page: 1 } })
  })
})
