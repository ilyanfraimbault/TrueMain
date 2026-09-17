import type { Ref } from 'vue'
import { createSSRApp } from 'vue'
import { renderToString } from 'vue/server-renderer'
import { afterEach, describe, expect, it, vi } from 'vitest'

// A lazily-hydrated child the way the champion pages use one: props bound from
// the snapshot bundle, `@vue:mounted` (compiled to `onVnodeMounted`) revealing
// the live value (#834/#837).
const ChampionCount = defineComponent({
  props: { champions: { type: Array as PropType<string[]>, required: true } },
  setup(props) {
    return () => h('p', `${props.champions.length} champions`)
  },
})

function consumer(live: Ref<string[]>, frozen: boolean) {
  return defineComponent({
    setup() {
      const snapshot = useLazyHydrationSnapshot(
        { champions: [] as string[] },
        () => ({ champions: live.value }),
      )
      return () =>
        h('div', [
          h(ChampionCount, frozen
            ? { ...snapshot.value, onVnodeMounted: snapshot.reveal }
            : { champions: live.value }),
        ])
    },
  })
}

// SSR renders with the client-only data still missing; the client hydrates once
// it has already arrived — the exact situation `hydrate-on-visible` creates.
async function serverThenClient(frozen: boolean) {
  const serverData = ref<string[]>([])
  const html = await renderToString(createSSRApp(consumer(serverData, frozen)))

  const container = document.createElement('div')
  container.innerHTML = html
  document.body.appendChild(container)

  const clientData = ref(['Aatrox', 'Darius', 'Garen'])
  const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
  const error = vi.spyOn(console, 'error').mockImplementation(() => {})
  const app = createSSRApp(consumer(clientData, frozen))
  app.mount(container)
  const mismatches = [...warn.mock.calls, ...error.mock.calls]
    .map(call => call.map(String).join(' '))
    .filter(message => /hydration/i.test(message))

  return { app, container, html, mismatches }
}

describe('useLazyHydrationSnapshot', () => {
  let cleanup: (() => void) | undefined

  afterEach(() => {
    cleanup?.()
    cleanup = undefined
    vi.restoreAllMocks()
  })

  it('hydrates against the SSR value, then reveals the live one after mount', async () => {
    const { app, container, html, mismatches } = await serverThenClient(true)
    cleanup = () => {
      app.unmount()
      container.remove()
    }

    expect(html).toContain('0 champions')
    expect(mismatches).toEqual([])

    await nextTick()
    expect(container.textContent).toBe('3 champions')
  })

  it('mismatches when the live value is bound directly (the bug the snapshot prevents)', async () => {
    const { app, container, mismatches } = await serverThenClient(false)
    cleanup = () => {
      app.unmount()
      container.remove()
    }

    expect(mismatches.length).toBeGreaterThan(0)
  })
})
