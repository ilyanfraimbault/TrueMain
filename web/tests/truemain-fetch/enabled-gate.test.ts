import { describe, expect, it, vi } from 'vitest'
import * as vue from 'vue'

/**
 * `enabled` is what lets the favorites page hold 30 cards' worth of requests
 * back until each card is near the viewport (#872). Two properties matter:
 *
 *  - while closed, *no* request exists — that is the fan-out bound itself;
 *  - while closed, the bundle still reads "initial loading", so the consumer
 *    keeps rendering skeletons. Short-circuiting into the cleared state would
 *    make a card that was never fetched look like a player with no games.
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

interface Payload { ok: true }

const PAYLOAD: Payload = { ok: true }

function harness(
  request: () => Promise<Payload | null>,
  enabled: vue.Ref<boolean>,
  nameTag: vue.MaybeRefOrGetter<string> = 'Sheiden-1234',
) {
  const data = vue.ref<Payload | null>(null)
  let loading!: vue.Ref<boolean>

  const component = vue.defineComponent({
    setup() {
      const { isInitialLoading } = useTruemainFetch<Payload>(nameTag, {
        enabled,
        request,
        validate: (response): response is Payload => Boolean(response),
        onResponse: (response) => { data.value = response },
        onClear: () => { data.value = null },
      })
      loading = isInitialLoading
      return () => vue.h('div', isInitialLoading.value ? 'skeleton' : 'content')
    },
  })

  vue.createApp(component).mount(document.createElement('div'))
  return { data, loading }
}

const flush = () => new Promise(resolve => setTimeout(resolve, 0))

describe('useTruemainFetch gating', () => {
  it('issues nothing while closed and holds the loading state', async () => {
    const request = vi.fn(async () => PAYLOAD)
    const { data, loading } = harness(request, vue.ref(false))
    await flush()

    expect(request).not.toHaveBeenCalled()
    expect(data.value).toBeNull()
    // Not "no data": a card whose fetch has not run must render as loading.
    expect(loading.value).toBe(true)
  })

  it('runs the skipped fetch once when the gate opens', async () => {
    const request = vi.fn(async () => PAYLOAD)
    const enabled = vue.ref(false)
    const { data, loading } = harness(request, enabled)
    await flush()

    enabled.value = true
    await vue.nextTick()
    await flush()

    expect(request).toHaveBeenCalledTimes(1)
    expect(data.value).toEqual(PAYLOAD)
    expect(loading.value).toBe(false)
  })

  it('does not queue up the inputs that changed while closed', async () => {
    const request = vi.fn(async () => PAYLOAD)
    const enabled = vue.ref(false)
    const nameTag = vue.ref('Sheiden-1234')
    harness(request, enabled, nameTag)

    nameTag.value = 'Other-4321'
    await vue.nextTick()
    await flush()
    expect(request).not.toHaveBeenCalled()

    enabled.value = true
    await vue.nextTick()
    await flush()
    // One request for the *current* name tag, not one per change.
    expect(request).toHaveBeenCalledTimes(1)
    expect(request).toHaveBeenCalledWith('Other-4321')
  })
})
