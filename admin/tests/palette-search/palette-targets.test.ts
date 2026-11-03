import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, defineComponent, h, nextTick, onMounted, reactive, ref, watch } from 'vue'
import AccountsSeedSingle from '~~/app/components/accounts/AccountsSeedSingle.vue'
import AccountsTrace from '~~/app/components/accounts/AccountsTrace.vue'
import { queryString, useQueryValue } from '~~/app/composables/useQueryValue'
import { useDeepLinkedDetail } from '~~/app/composables/useDeepLinkedDetail'

// The pages a ⌘K result lands on (#2290) read the value from the query string —
// on arrival, and again when the palette jumps while the page is already open.

const stub = (tag: string) => defineComponent({
  props: ['label', 'modelValue'],
  setup(props, { slots, attrs }) {
    return () => h(tag, { ...attrs, 'data-value': props.modelValue }, [slots.default?.(), slots.info?.()])
  },
})

const ui = Object.fromEntries(
  ['UCard', 'UInput', 'USelect', 'UButton', 'USkeleton', 'UIcon', 'UForm', 'UFormField', 'UAlert', 'UBadge', 'UStepper', 'PanelTitle', 'FetchErrorAlert']
    .map(name => [name, stub('div')]),
)

const query = ref<Record<string, unknown>>({})
const replace = vi.fn(({ query: next }: { query: Record<string, unknown> }) => {
  query.value = next
})

beforeEach(() => {
  query.value = {}
  replace.mockClear()
  // Vue's auto-imports, which the app gets from Nuxt.
  for (const [name, fn] of Object.entries({ computed, onMounted, reactive, ref, watch })) {
    vi.stubGlobal(name, fn)
  }
  vi.stubGlobal('useRoute', () => ({ get query() { return query.value } }))
  vi.stubGlobal('useRouter', () => ({ replace }))
  vi.stubGlobal('useQueryValue', useQueryValue)
  vi.stubGlobal('queryString', queryString)
  vi.stubGlobal('ALL', 'all')
  vi.stubGlobal('REGION_ITEMS', [])
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('queryString', () => {
  it('takes the first non-empty string', () => {
    expect(queryString(['a', 'b'])).toBe('a')
    expect(queryString('  ')).toBeNull()
    expect(queryString(undefined)).toBeNull()
    expect(queryString(3)).toBeNull()
  })
})

describe('AccountsTrace', () => {
  it('traces the Riot ID it arrives with, then the next one the palette sends', async () => {
    const getAccountExplorer = vi.fn().mockResolvedValue(null)
    vi.stubGlobal('getAccountExplorer', getAccountExplorer)
    query.value = { view: 'trace', riotId: 'Faker#KR1', region: 'KR' }

    mount(AccountsTrace, { global: { components: ui } })
    await flushPromises()
    expect(getAccountExplorer).toHaveBeenCalledWith('Faker#KR1', 'KR')

    query.value = { view: 'trace', riotId: 'Caps#EUW' }
    await flushPromises()
    expect(getAccountExplorer).toHaveBeenLastCalledWith('Caps#EUW', undefined)
    expect(getAccountExplorer).toHaveBeenCalledTimes(2)
  })

  it('does not call the API for a malformed Riot ID', async () => {
    const getAccountExplorer = vi.fn()
    vi.stubGlobal('getAccountExplorer', getAccountExplorer)
    query.value = { riotId: 'nohash' }

    mount(AccountsTrace, { global: { components: ui } })
    await flushPromises()
    expect(getAccountExplorer).not.toHaveBeenCalled()
  })
})

describe('AccountsSeedSingle', () => {
  it('prefills the name fields from ?riotId=, either spelling', async () => {
    vi.stubGlobal('useSeedTracking', () => ({
      submitting: ref(false),
      tracked: ref(null),
      submitError: ref(null),
      polling: ref(false),
      polledOut: ref(false),
      submit: vi.fn(),
    }))
    vi.stubGlobal('seedStatusColor', () => 'neutral')
    vi.stubGlobal('seedStatusIcon', () => 'i-lucide-circle')
    query.value = { view: 'seed', riotId: 'Faker#KR1' }

    const wrapper = mount(AccountsSeedSingle, { global: { components: ui } })
    await nextTick()
    const values = () => wrapper.findAll('[data-value]').map(node => node.attributes('data-value'))
    expect(values()).toEqual(expect.arrayContaining(['Faker', 'KR1']))

    query.value = { view: 'seed', riotId: 'Hide on bush-KR2' }
    await nextTick()
    await nextTick()
    expect(values()).toEqual(expect.arrayContaining(['Hide on bush', 'KR2']))
  })
})

describe('useDeepLinkedDetail', () => {
  it('opens the id a later query names, and not again for its own mirror', async () => {
    const fetch = vi.fn().mockResolvedValue({ id: 'x' })
    const onDeepLink = vi.fn()
    let detail!: ReturnType<typeof useDeepLinkedDetail<{ id: string }>>
    mount(defineComponent({
      setup() {
        detail = useDeepLinkedDetail({ queryKey: 'match', fetch, notFoundMessage: id => id, loadErrorMessage: 'x', onDeepLink })
        return () => h('div')
      },
    }))
    await flushPromises()
    expect(fetch).not.toHaveBeenCalled()

    query.value = { match: 'EUW1_7012345678' }
    await flushPromises()
    expect(fetch).toHaveBeenCalledWith('EUW1_7012345678')
    expect(onDeepLink).toHaveBeenCalledWith('EUW1_7012345678')
    expect(detail.detailOpen.value).toBe(true)

    await detail.openDetail('KR_7301234567')
    await flushPromises()
    expect(fetch).toHaveBeenCalledTimes(2)
    expect(query.value.match).toBe('KR_7301234567')
  })
})
