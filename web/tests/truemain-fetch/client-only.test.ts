import { describe, expect, it, vi } from 'vitest'
import * as vue from 'vue'
import { renderToString } from 'vue/server-renderer'

/**
 * Regression cover for #862: a full SSR load of `/truemains/{nameTag}` hung in
 * its skeletons forever because `useTruemainFetch` — documented as client-only —
 * also issued its request during SSR, so the server-rendered markup carried the
 * resolved profile while the client's first (hydration) render was the skeleton
 * branch. The resulting hydration mismatch crashed Vue's patch loop.
 *
 * The invariant is therefore about *sides*, not about the payload: the request
 * must not exist during a server render, and must fire once the component
 * mounts. `renderToString` and `mount` are the two sides, and mounted hooks are
 * exactly what separates them — no Nuxt runtime needed.
 *
 * `useTruemainFetch` is app code written against Nuxt's auto-imports, so its
 * `ref` / `computed` / `watch` / `toValue` / `onMounted` references are bare
 * identifiers that resolve as globals here. Seeding them from the same `vue`
 * instance the test uses stands in for the imports Nuxt injects at build time,
 * and has to happen before the module is evaluated — hence the dynamic import
 * below.
 */
Object.assign(globalThis, {
  ref: vue.ref,
  computed: vue.computed,
  watch: vue.watch,
  toValue: vue.toValue,
  onMounted: vue.onMounted,
})

const { useTruemainFetch } = await import('~/composables/useTruemainFetch')

interface Payload { ok: true }

const PAYLOAD: Payload = { ok: true }

/**
 * A minimal component standing in for the profile page: it binds the same
 * loading ref the real page uses to pick between skeleton and content, so the
 * rendered string tells us which branch a given side produced.
 */
function harness(
  request: () => Promise<Payload | null>,
  nameTag: vue.MaybeRefOrGetter<string> = 'Sheiden-1234',
  watchSources?: vue.WatchSource[],
) {
  const data = vue.ref<Payload | null>(null)

  const component = vue.defineComponent({
    setup() {
      const { isInitialLoading } = useTruemainFetch<Payload>(nameTag, {
        request,
        validate: (response): response is Payload => Boolean(response),
        onResponse: (response) => { data.value = response },
        onClear: () => { data.value = null },
        watch: watchSources,
      })
      return () => vue.h('div', isInitialLoading.value ? 'skeleton' : 'content')
    },
  })

  return { component, data }
}

/** Let any accidentally-started fetch settle before asserting it never ran. */
const flush = () => new Promise(resolve => setTimeout(resolve, 0))

describe('useTruemainFetch is client-only', () => {
  it('issues no request during a server render and emits the loading branch', async () => {
    const request = vi.fn(async () => PAYLOAD)
    const { component, data } = harness(request)

    const html = await renderToString(vue.createSSRApp(component))
    await flush()

    expect(request).not.toHaveBeenCalled()
    expect(data.value).toBeNull()
    // The SSR markup the client hydrates against must be the same branch the
    // client's first render produces, which is always the loading one.
    expect(html).toContain('skeleton')
    expect(html).not.toContain('content')
  })

  it('fetches once the component mounts on the client', async () => {
    const request = vi.fn(async () => PAYLOAD)
    const { component, data } = harness(request)

    vue.createApp(component).mount(document.createElement('div'))
    await flush()

    expect(request).toHaveBeenCalledTimes(1)
    expect(data.value).toEqual(PAYLOAD)
  })

  it('still refires on a watched input changing after mount', async () => {
    const request = vi.fn(async () => PAYLOAD)
    const page = vue.ref(1)
    const { component } = harness(request, 'Sheiden-1234', [page])

    vue.createApp(component).mount(document.createElement('div'))
    await flush()
    expect(request).toHaveBeenCalledTimes(1)

    page.value = 2
    await vue.nextTick()
    await flush()
    expect(request).toHaveBeenCalledTimes(2)
  })
})
