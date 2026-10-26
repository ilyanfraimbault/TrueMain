import type { AuditEvent, AuditEventsResponse } from '~~/shared/types/ops'
import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import AuditEventList from '~~/app/components/audit/AuditEventList.vue'
import KnownPlayersHistory from '~~/app/components/known-players/KnownPlayersHistory.vue'
import { useAuditList } from '~~/app/composables/useAuditList'
import { getAuditEvents, useAuditEvents } from '~~/app/composables/useOpsAudit'
import AuditPage from '~~/app/pages/audit.vue'
import { auditChips, auditTarget } from '~~/shared/utils/audit'

// The operator-action trail (#2288): its target links and chips, the timeline, the
// known-player History, and the Audit page's filters.

const event = (overrides: Partial<AuditEvent> = {}): AuditEvent => ({
  id: 'e1',
  timestampUtc: '2026-10-25T10:00:00Z',
  actor: 'operator',
  action: 'rename_persona',
  label: 'Renamed a known player',
  targetType: 'Persona',
  targetId: 'p1',
  metadata: { from: 'Caps', to: 'Caps (G2)', slug: 'caps' },
  ...overrides,
})

const response = (entries: AuditEvent[], total = entries.length): AuditEventsResponse => ({
  entries,
  total,
  page: 1,
  pageSize: 50,
  actions: [{ name: 'rename_persona', label: 'Renamed a known player' }],
  actors: ['operator', 'auto', 'ingestor'],
  targetTypes: ['Persona', 'SeedRequest'],
})

const stub = (tag: string, text?: (props: Record<string, unknown>) => string) => defineComponent({
  props: ['label', 'to', 'title', 'items', 'modelValue'],
  emits: ['click'],
  setup(props, { slots, emit, attrs }) {
    return () => h(tag, { ...attrs, href: props.to, onClick: () => emit('click') }, [
      text ? text(props) : '',
      slots.default?.(),
      ...Object.entries(slots).filter(([name]) => name !== 'default').map(([, slot]) => slot?.()),
    ])
  },
})

const components = {
  UBadge: stub('mark', p => String(p.label ?? '')),
  ULink: stub('a'),
  UButton: stub('button', p => String(p.label ?? '')),
  USelect: stub('select'),
  UInput: stub('input'),
  UPagination: stub('nav'),
  UDashboardPanel: stub('div'),
  UDashboardNavbar: stub('header'),
  UDashboardToolbar: stub('div'),
  UDashboardSidebarCollapse: stub('i'),
  FetchErrorAlert: stub('aside', p => String(p.title ?? '')),
  AuditEventList,
}
const global = { components }

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('auditTarget / auditChips', () => {
  it('links a known player, a conflict and a seed request where they live', () => {
    expect(auditTarget(event())).toEqual({ label: 'caps', to: '/known-players?player=p1' })
    expect(auditTarget(event({ targetType: 'PersonaAccountConflict', targetId: 'c 1', metadata: {} })))
      .toEqual({ label: 'conflict', to: '/known-players?status=resolved&conflict=c%201' })
    expect(auditTarget(event({ targetType: 'SeedRequest', metadata: { gameName: 'Faker', tagLine: 'KR1' } })))
      .toEqual({ label: 'Faker#KR1', to: '/accounts?view=seed' })
    expect(auditTarget(event({ targetType: 'SeedRequest', metadata: {} })).label).toBe('seed request')
    expect(auditTarget(event({ targetType: 'Persona', targetId: null, metadata: {} }))).toEqual({ label: 'known player', to: null })
    expect(auditTarget(event({ targetType: null }))).toEqual({ label: '', to: null })
    expect(auditTarget(event({ targetType: 'Other' }))).toEqual({ label: 'Other', to: null })
  })

  it('folds a rename into one chip and drops what the target already shows', () => {
    expect(auditChips(event())).toEqual([{ key: 'name', value: 'Caps → Caps (G2)' }])
    expect(auditChips(event({ metadata: { url: 'https://x.com/caps', slug: 'caps' } }))).toEqual([{ key: 'url', value: 'https://x.com/caps' }])
    expect(auditChips(event({ targetType: 'SeedRequest', metadata: { gameName: 'Faker', tagLine: 'KR1', platformId: 'KR' } })))
      .toEqual([{ key: 'platformId', value: 'KR' }])
    expect(auditChips(event({ targetType: 'SeedRequest', metadata: { gameName: 'Faker' } }))).toEqual([{ key: 'gameName', value: 'Faker' }])
  })
})

describe('AuditEventList', () => {
  it('draws when, who, what, the target link and the chips', () => {
    const wrapper = mount(AuditEventList, { global, props: { events: [event()], pending: false, emptyText: 'None.' } })
    const row = wrapper.find('[data-event]')
    expect(row.find('[data-actor]').text()).toBe('operator')
    expect(row.find('[data-label]').text()).toBe('Renamed a known player')
    expect(row.find('a[data-target]').attributes('href')).toBe('/known-players?player=p1')
    expect(row.find('[data-chip]').text()).toContain('Caps → Caps (G2)')
  })

  it('shows an unlinked target as text, hides it on demand, and says why it is empty', () => {
    const plain = mount(AuditEventList, { global, props: { events: [event({ targetType: 'Other' })], pending: false, emptyText: 'None.' } })
    expect(plain.find('span[data-target]').text()).toBe('Other')
    const hidden = mount(AuditEventList, { global, props: { events: [event()], pending: false, emptyText: 'None.', showTarget: false } })
    expect(hidden.find('[data-target]').exists()).toBe(false)
    expect(mount(AuditEventList, { global, props: { events: [], pending: false, emptyText: 'None.' } }).find('[data-empty]').text()).toBe('None.')
    expect(mount(AuditEventList, { global, props: { events: [], pending: true, emptyText: 'None.' } }).find('[data-empty]').exists()).toBe(false)
  })
})

describe('KnownPlayersHistory', () => {
  it('reads the player\'s own trail and reloads when the panel edits it', async () => {
    const fetchEvents = vi.fn().mockResolvedValue(response([event()], 30))
    vi.stubGlobal('getAuditEvents', fetchEvents)
    const wrapper = mount(KnownPlayersHistory, { global, props: { personaId: 'p1', version: 0 } })
    await flushPromises()
    expect(fetchEvents).toHaveBeenCalledWith({ targetType: 'Persona', targetId: 'p1', pageSize: 20 })
    expect(wrapper.text()).toContain('History · 30')
    expect(wrapper.find('[data-history-all]').attributes('href')).toBe('/audit?targetType=Persona&targetId=p1')
    expect(wrapper.find('[data-target]').exists()).toBe(false)

    await wrapper.setProps({ version: 1 })
    await flushPromises()
    expect(fetchEvents).toHaveBeenCalledTimes(2)
  })

  it('says when the history failed to load', async () => {
    vi.stubGlobal('getAuditEvents', vi.fn().mockRejectedValue(new Error('down')))
    const wrapper = mount(KnownPlayersHistory, { global, props: { personaId: 'p1', version: 0 } })
    await flushPromises()
    expect(wrapper.find('aside').text()).toBe('Failed to load this player\'s history')
  })
})

describe('useAuditList and the Audit page', () => {
  const query = ref<Record<string, unknown>>({})
  const replace = vi.fn()
  let lastFilters: { value: Record<string, unknown> }

  beforeEach(() => {
    query.value = { targetType: 'Persona', targetId: 'p1' }
    replace.mockReset()
    vi.stubGlobal('useRoute', () => ({ get query() { return query.value } }))
    vi.stubGlobal('useRouter', () => ({ replace }))
    vi.stubGlobal('refDebounced', (source: unknown) => source)
    vi.stubGlobal('ALL', 'all')
    vi.stubGlobal('sinceToIso', () => '2026-10-25T00:00:00.000Z')
    vi.stubGlobal('useAuditEvents', (filters: { value: Record<string, unknown> }) => {
      lastFilters = filters
      return { data: ref(response([event()], 120)), pending: ref(false), error: ref(null), refresh: vi.fn() }
    })
    vi.stubGlobal('useAuditList', useAuditList)
    vi.stubGlobal('SINCE_ITEMS', [])
  })

  it('starts on the linked target and builds the filters the endpoint reads', async () => {
    const list = useAuditList()
    expect(list.hasActiveFilters.value).toBe(true)
    expect(lastFilters.value).toMatchObject({ targetType: 'Persona', targetId: 'p1', page: 1, pageSize: 50 })
    list.action.value = 'rename_persona'
    list.actor.value = 'operator'
    list.sinceWindow.value = '24h'
    list.searchInput.value = ' caps '
    expect(lastFilters.value).toMatchObject({ action: 'rename_persona', actor: 'operator', since: '2026-10-25T00:00:00.000Z', search: 'caps' })
    expect(list.actionItems.value.map(i => i.label)).toEqual(['All actions', 'Renamed a known player'])
    expect(list.actorItems.value).toHaveLength(4)
    expect(list.targetTypeItems.value).toHaveLength(3)
    expect(list.serverPageSize.value).toBe(50)

    list.page.value = 3
    list.resetFilters()
    await flushPromises()
    expect(list.page.value).toBe(1)
    expect(list.hasActiveFilters.value).toBe(false)
    expect(replace).toHaveBeenCalledWith({ query: {} })
    expect(lastFilters.value).toMatchObject({ action: undefined, targetId: undefined, since: undefined, search: undefined })
  })

  it('renders the trail with its count, the target filter and the pager', async () => {
    const wrapper = mount(AuditPage, { global })
    expect(wrapper.text()).toContain('120 actions')
    expect(wrapper.find('[data-target-filter]').text()).toContain('p1')
    expect(wrapper.findAll('[data-event]')).toHaveLength(1)
    expect(wrapper.find('nav').exists()).toBe(true)
    await wrapper.find('[data-target-filter] button').trigger('click')
    expect(replace).toHaveBeenCalled()
  })

  it('exposes the endpoint to the page and to the History', () => {
    const useOps = vi.fn(() => 'fetch')
    const fetcher = vi.fn()
    vi.stubGlobal('useOps', useOps)
    vi.stubGlobal('$fetch', fetcher)
    vi.stubGlobal('toValue', (v: unknown) => (typeof v === 'function' ? v() : v))
    expect(useAuditEvents(() => ({ action: 'x' }))).toBe('fetch')
    const [path, filters] = useOps.mock.calls[0] as unknown as [string, () => unknown]
    expect(path).toBe('/audit-events')
    expect(filters()).toEqual({ action: 'x' })
    getAuditEvents({ targetId: 'p1' })
    expect(fetcher).toHaveBeenCalledWith('/api/ops/audit-events', { query: { targetId: 'p1' } })
  })
})
