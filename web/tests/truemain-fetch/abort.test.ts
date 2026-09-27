import { describe, expect, it } from 'vitest'
import * as vue from 'vue'

/**
 * The request token in `useTruemainFetch` stops a superseded response from being
 * written; it does not stop it from being fetched. Stepping through pages or
 * leaving a profile used to leave every superseded request running to
 * completion, which is what bogged the site down on fast navigation (#1712).
 * The composable now hands each request a signal and aborts it the moment the
 * request stops mattering.
 *
 * Same auto-import stand-in as `client-only.test.ts` in this folder.
 */
Object.assign(globalThis, {
  ref: vue.ref,
  computed: vue.computed,
  watch: vue.watch,
  toValue: vue.toValue,
  onMounted: vue.onMounted,
  onScopeDispose: vue.onScopeDispose,
})

const { useTruemainFetch } = await import('~/composables/useTruemainFetch')

interface Payload { page: number }

function harness(page: vue.Ref<number>, nameTag: vue.Ref<string>) {
  const signals: AbortSignal[] = []
  let error!: vue.Ref<unknown>

  const component = vue.defineComponent({
    setup() {
      const bundle = useTruemainFetch<Payload>(nameTag, {
        // Never settles on its own — only an abort can end it, like a slow
        // backend read the visitor has already walked away from.
        request: (_tag, signal) => {
          signals.push(signal)
          return new Promise<Payload>((_resolve, reject) => {
            signal.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')))
          })
        },
        watch: [page],
        validate: (response): response is Payload => Boolean(response),
        onResponse: () => {},
        onClear: () => {},
      })
      error = bundle.error
      return () => vue.h('div')
    },
  })

  const app = vue.createApp(component)
  app.mount(document.createElement('div'))
  return { signals, error: () => error.value, unmount: () => app.unmount() }
}

const drain = () => new Promise(resolve => setTimeout(resolve, 0))

describe('useTruemainFetch cancellation', () => {
  it('aborts the superseded request when its inputs change', async () => {
    const page = vue.ref(1)
    const { signals, error } = harness(page, vue.ref('Sheiden-1234'))

    await vue.nextTick()
    page.value = 2
    await vue.nextTick()
    await drain()

    expect(signals).toHaveLength(2)
    expect(signals[0]!.aborted).toBe(true)
    expect(signals[1]!.aborted).toBe(false)
    // The cancellation is ours, not a failure to show.
    expect(error()).toBeNull()
  })

  it('aborts the in-flight request when the name tag empties', async () => {
    const nameTag = vue.ref('Sheiden-1234')
    const { signals } = harness(vue.ref(1), nameTag)

    await vue.nextTick()
    nameTag.value = ''
    await vue.nextTick()

    expect(signals).toHaveLength(1)
    expect(signals[0]!.aborted).toBe(true)
  })

  it('aborts the in-flight request when the component unmounts', async () => {
    const { signals, unmount } = harness(vue.ref(1), vue.ref('Sheiden-1234'))

    await vue.nextTick()
    unmount()

    expect(signals).toHaveLength(1)
    expect(signals[0]!.aborted).toBe(true)
  })
})
