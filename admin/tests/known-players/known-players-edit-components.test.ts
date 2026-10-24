import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import KnownPlayersAccountsPanel from '~~/app/components/known-players/KnownPlayersAccountsPanel.vue'
import KnownPlayersCreate from '~~/app/components/known-players/KnownPlayersCreate.vue'
import KnownPlayersDecisions from '~~/app/components/known-players/KnownPlayersDecisions.vue'
import KnownPlayersDirectory from '~~/app/components/known-players/KnownPlayersDirectory.vue'
import KnownPlayersEditor from '~~/app/components/known-players/KnownPlayersEditor.vue'
import KnownPlayersLinksPanel from '~~/app/components/known-players/KnownPlayersLinksPanel.vue'
import KnownPlayersList from '~~/app/components/known-players/KnownPlayersList.vue'
import KnownPlayersRemoval from '~~/app/components/known-players/KnownPlayersRemoval.vue'
import { detail, row } from './known-players-edit-fixtures'

// Nuxt UI is auto-imported in the app; here stand-ins render the props a test reads.
const stub = (tag: string, text?: (props: Record<string, unknown>) => string) => defineComponent({
  props: ['label', 'color', 'variant', 'loading', 'disabled', 'icon', 'title', 'description', 'error', 'to', 'type'],
  emits: ['click'],
  setup(props, { slots, emit, attrs }) {
    return () => h(tag, {
      ...attrs,
      'data-stub': tag,
      'disabled': tag === 'button' && props.disabled ? true : undefined,
      'type': tag === 'button' ? (props.type ?? 'button') : undefined,
      'href': props.to,
      'onClick': () => emit('click'),
    }, [text ? text(props) : '', slots.header?.(), slots.default?.(), slots.body?.()])
  },
})

const UInput = defineComponent({
  props: ['modelValue'],
  emits: ['update:modelValue'],
  setup(props, { emit, attrs }) {
    return () => h('input', {
      'aria-label': attrs['aria-label'],
      'placeholder': attrs.placeholder,
      'value': props.modelValue,
      'onInput': (e: Event) => emit('update:modelValue', (e.target as HTMLInputElement).value),
    })
  },
})

const USelect = defineComponent({
  props: ['modelValue', 'items'],
  emits: ['update:modelValue'],
  setup(props, { emit, attrs }) {
    return () => h('select', {
      'aria-label': attrs['aria-label'],
      'value': props.modelValue,
      'onChange': (e: Event) => emit('update:modelValue', (e.target as HTMLSelectElement).value),
    }, (props.items as { label: string, value: string }[]).map(i => h('option', { value: i.value }, i.label)))
  },
})

const UFormField = defineComponent({
  props: ['error', 'label', 'help'],
  setup(props, { slots }) {
    return () => h('div', { 'data-field': props.label }, [slots.default?.(), props.error ? h('small', { 'data-error': '' }, String(props.error)) : null])
  },
})

const ui = {
  UBadge: stub('mark', p => String(p.label ?? '')),
  UButton: stub('button', p => String(p.label ?? '')),
  UIcon: stub('i'),
  ULink: stub('a'),
  UAlert: stub('aside', p => String(p.title ?? '')),
  USeparator: stub('hr'),
  UCard: stub('section'),
  UPagination: stub('nav'),
  USlideover: stub('div'),
  UModal: stub('div'),
  FetchErrorAlert: stub('aside', p => String(p.title ?? '')),
  UInput,
  USelect,
  UFormField,
}

const global = { components: ui }
const button = (wrapper: ReturnType<typeof mount>, selector: string) => wrapper.find(`button${selector}`)

describe('KnownPlayersList', () => {
  it('draws who, what is tracked and what needs a decision, and opens the clicked row', async () => {
    const wrapper = mount(KnownPlayersList, {
      global,
      props: {
        personas: [
          row(),
          row({ id: 'p2', slug: 'gone', displayName: 'Gone', removedAtUtc: '2026-10-23T00:00:00Z', trackedAccounts: 0 }),
          row({ id: 'p3', slug: 'faker', displayName: 'Faker', unresolvedAccounts: 1, openConflicts: 2, displayNameLocked: true }),
        ],
        pending: false,
        emptyText: 'Nothing.',
      },
    })
    const rows = wrapper.findAll('[data-row]')
    expect(rows).toHaveLength(3)
    expect(rows[0]!.text()).toContain('Caps')
    expect(rows[0]!.text()).toContain('from Leaguepedia')
    expect(rows[0]!.text()).toContain('2 tracked')
    expect(rows[1]!.find('[data-removed]').exists()).toBe(true)
    expect(rows[1]!.text()).toContain('No account')
    expect(rows[2]!.find('[data-conflicts]').text()).toBe('2 open')
    expect(rows[2]!.text()).toContain('2 tracked · 1 untracked')

    await rows[2]!.trigger('click')
    await rows[0]!.trigger('keydown', { key: 'Enter' })
    expect(wrapper.emitted('open')!.map(([p]) => (p as { id: string }).id)).toEqual(['p3', 'p1'])
  })

  it('says why the list is empty, but not while it loads', () => {
    expect(mount(KnownPlayersList, { global, props: { personas: [], pending: false, emptyText: 'None.' } }).find('[data-empty]').text()).toBe('None.')
    expect(mount(KnownPlayersList, { global, props: { personas: [], pending: true, emptyText: 'None.' } }).find('[data-empty]').exists()).toBe(false)
  })
})

describe('KnownPlayersAccountsPanel', () => {
  it('unlinks on the second click only', async () => {
    const wrapper = mount(KnownPlayersAccountsPanel, { global, props: { accounts: detail().accounts, busy: false } })
    expect(wrapper.text()).toContain('Accounts · 2 (1 tracked)')
    expect(wrapper.text()).toContain('Dpm · untracked')
    await button(wrapper, '[data-unlink="a2"]').trigger('click')
    expect(wrapper.emitted('unlink')).toBeUndefined()
    expect(button(wrapper, '[data-unlink="a2"]').text()).toBe('Confirm unlink')
    await button(wrapper, '[data-unlink="a2"]').trigger('click')
    expect(wrapper.emitted('unlink')![0]![0]).toMatchObject({ id: 'a2' })
  })

  it('adds a typed Riot ID on its platform, and refuses a malformed one', async () => {
    const wrapper = mount(KnownPlayersAccountsPanel, { global, props: { accounts: [], busy: false } })
    expect(wrapper.text()).toContain('No account.')
    await wrapper.find('input').setValue('Caps')
    expect(wrapper.find('[data-error]').text()).toBe('Missing "#tagLine"')
    await wrapper.find('form').trigger('submit')
    expect(wrapper.emitted('add')).toBeUndefined()

    await wrapper.find('input').setValue('G2 Caps#1323')
    await wrapper.find('select').setValue('KR')
    await wrapper.find('form').trigger('submit')
    expect(wrapper.emitted('add')).toEqual([[{ gameName: 'G2 Caps', tagLine: '1323', platformId: 'KR' }]])
    expect((wrapper.find('input').element as HTMLInputElement).value).toBe('')
  })

  it('does nothing while an edit is in flight', async () => {
    const wrapper = mount(KnownPlayersAccountsPanel, { global, props: { accounts: detail().accounts, busy: true } })
    await button(wrapper, '[data-unlink="a1"]').trigger('click')
    await button(wrapper, '[data-unlink="a1"]').trigger('click')
    await wrapper.find('input').setValue('G2 Caps#1323')
    await wrapper.find('form').trigger('submit')
    expect(wrapper.emitted('unlink')).toBeUndefined()
    expect(wrapper.emitted('add')).toBeUndefined()
  })
})

describe('KnownPlayersLinksPanel', () => {
  it('removes on the second click and adds a valid URL of a kind', async () => {
    const wrapper = mount(KnownPlayersLinksPanel, { global, props: { links: detail().links, busy: false } })
    expect(wrapper.find('a').attributes('href')).toBe('https://www.twitch.tv/caps')
    await button(wrapper, '[data-remove-link="l1"]').trigger('click')
    await button(wrapper, '[data-remove-link="l1"]').trigger('click')
    expect(wrapper.emitted('remove')![0]![0]).toMatchObject({ id: 'l1' })

    await wrapper.find('input').setValue('youtube.com/@caps')
    expect(wrapper.find('[data-error]').exists()).toBe(true)
    await wrapper.find('form').trigger('submit')
    expect(wrapper.emitted('add')).toBeUndefined()

    await wrapper.find('select').setValue('youtube')
    await wrapper.find('input').setValue(' https://www.youtube.com/@caps ')
    await wrapper.find('form').trigger('submit')
    expect(wrapper.emitted('add')).toEqual([[{ kind: 'youtube', url: 'https://www.youtube.com/@caps' }]])
  })

  it('says when there is no link', () => {
    expect(mount(KnownPlayersLinksPanel, { global, props: { links: [], busy: false } }).text()).toContain('No link.')
  })
})

describe('KnownPlayersDecisions', () => {
  it('lists open conflicts with a settle link, exclusions with their reason, and removed links', () => {
    const wrapper = mount(KnownPlayersDecisions, {
      global,
      props: {
        conflicts: [{ id: 'c1', role: 'owner', gameName: 'G2 Caps', tagLine: '1323', platformId: 'EUW1', otherSlug: 'caps-dpm', otherDisplayName: 'Caps', source: 'Dpm', lastSeenAtUtc: '2026-10-22T00:00:00Z' }],
        exclusions: [{ gameName: 'Old', tagLine: 'EUW', platformId: 'EUW1', reason: 'takedown', createdAtUtc: '2026-10-22T00:00:00Z' }],
        removedLinks: [{ url: 'https://x.com/caps', createdAtUtc: '2026-10-22T00:00:00Z' }],
      },
    })
    expect(wrapper.find('[data-conflicts]').text()).toContain('claimed by Caps')
    expect(wrapper.find('[data-conflicts] a').attributes('href')).toBe('/known-players?status=open&conflict=c1')
    expect(wrapper.find('[data-exclusions]').text()).toContain('taken down')
    expect(wrapper.find('[data-removed-links]').text()).toContain('https://x.com/caps')
  })

  it('shows nothing when nothing holds the player', () => {
    const wrapper = mount(KnownPlayersDecisions, { global, props: { conflicts: [], exclusions: [], removedLinks: [] } })
    expect(wrapper.find('section').exists()).toBe(false)
  })
})

describe('KnownPlayersRemoval', () => {
  it('arms then sends a removal or a takedown', async () => {
    const wrapper = mount(KnownPlayersRemoval, { global, props: { displayName: 'Caps', busy: false } })
    await button(wrapper, '[data-action="takedown"]').trigger('click')
    expect(button(wrapper, '[data-action="takedown"]').text()).toBe('Confirm: Take down')
    await button(wrapper, '[data-action="remove"]').trigger('click')
    await button(wrapper, '[data-action="remove"]').trigger('click')
    expect(wrapper.emitted('remove')).toEqual([[false]])
    await button(wrapper, '[data-action="takedown"]').trigger('click')
    await button(wrapper, '[data-action="takedown"]').trigger('click')
    expect(wrapper.emitted('remove')).toEqual([[false], [true]])
  })

  it('sends nothing while busy', async () => {
    const wrapper = mount(KnownPlayersRemoval, { global, props: { displayName: 'Caps', busy: true } })
    await button(wrapper, '[data-action="remove"]').trigger('click')
    await button(wrapper, '[data-action="remove"]').trigger('click')
    expect(wrapper.emitted('remove')).toBeUndefined()
  })
})

// The containers call the ops composables, auto-imported in the app; here they are globals.
const toast = { success: vi.fn(), warning: vi.fn(), failure: vi.fn() }
const api = {
  getPersona: vi.fn(),
  renamePersona: vi.fn(),
  addPersonaAccount: vi.fn(),
  unlinkPersonaAccount: vi.fn(),
  addPersonaLink: vi.fn(),
  removePersonaLink: vi.fn(),
  removePersona: vi.fn(),
  createPersona: vi.fn(),
}

beforeEach(() => {
  for (const [name, fn] of Object.entries(api)) {
    fn.mockReset()
    vi.stubGlobal(name, fn)
  }
  Object.values(toast).forEach(fn => fn.mockReset())
  vi.stubGlobal('useActionToast', () => toast)
})

afterEach(() => {
  vi.unstubAllGlobals()
})

const editorGlobal = {
  components: { ...ui, KnownPlayersAccountsPanel, KnownPlayersLinksPanel, KnownPlayersDecisions, KnownPlayersRemoval },
}

describe('KnownPlayersEditor', () => {
  it('loads the player and runs each edit, reloading after it', async () => {
    api.getPersona.mockResolvedValue(detail())
    api.addPersonaAccount.mockResolvedValue({ id: 'p1', slug: 'caps', accounts: [{ gameName: 'X', tagLine: 'Y', platformId: 'EUW1', status: 'conflict' }] })
    api.unlinkPersonaAccount.mockResolvedValue({ id: 'p1', slug: 'caps', accounts: [] })
    api.removePersonaLink.mockResolvedValue({ id: 'p1', slug: 'caps', accounts: [] })
    api.addPersonaLink.mockRejectedValue({ data: { detail: 'This link is already listed.' } })
    api.renamePersona.mockResolvedValue({ id: 'p1', slug: 'caps', accounts: [] })
    const wrapper = mount(KnownPlayersEditor, { global: editorGlobal, props: { personaId: 'p1' } })
    await flushPromises()
    expect(wrapper.text()).toContain('from Leaguepedia')

    const accounts = wrapper.findComponent(KnownPlayersAccountsPanel)
    accounts.vm.$emit('add', { gameName: 'X', tagLine: 'Y', platformId: 'EUW1' })
    await flushPromises()
    expect(api.addPersonaAccount).toHaveBeenCalledWith('p1', { gameName: 'X', tagLine: 'Y', platformId: 'EUW1' })
    expect(toast.warning).toHaveBeenCalledWith('Not added: another known player holds it', expect.any(String))

    accounts.vm.$emit('unlink', detail().accounts[1])
    await flushPromises()
    expect(api.unlinkPersonaAccount).toHaveBeenCalledWith('p1', 'a2')
    expect(toast.success).toHaveBeenCalledWith('Caps Alt#EUW unlinked', expect.any(String))

    const links = wrapper.findComponent(KnownPlayersLinksPanel)
    links.vm.$emit('remove', detail().links[0])
    links.vm.$emit('add', { kind: 'twitch', url: 'https://www.twitch.tv/caps' })
    await flushPromises()
    expect(api.removePersonaLink).toHaveBeenCalledWith('p1', 'l1')
    expect(toast.failure).toHaveBeenCalledWith('Link not added', 'This link is already listed.')

    await wrapper.find('button[data-rename]').trigger('click')
    await wrapper.find('input[aria-label="Name"]').setValue('Caps (G2)')
    await wrapper.find('button[data-rename-save]').trigger('click')
    await flushPromises()
    expect(api.renamePersona).toHaveBeenCalledWith('p1', 'Caps (G2)')
    expect(wrapper.emitted('changed')).toHaveLength(4)
    expect(api.getPersona.mock.calls.length).toBeGreaterThanOrEqual(6)
  })

  it('removes the player, and shows a removed or merged one without edits', async () => {
    api.getPersona.mockResolvedValueOnce(detail()).mockResolvedValue(detail({ removedAtUtc: '2026-10-24T00:00:00Z', accounts: [], links: [] }))
    api.removePersona.mockResolvedValue({ id: 'p1', slug: 'caps', accounts: [] })
    const wrapper = mount(KnownPlayersEditor, { global: editorGlobal, props: { personaId: 'p1' } })
    await flushPromises()
    wrapper.findComponent(KnownPlayersRemoval).vm.$emit('remove', true)
    await flushPromises()
    expect(api.removePersona).toHaveBeenCalledWith('p1', true)
    expect(toast.success).toHaveBeenCalledWith('Caps taken down', expect.any(String))
    expect(wrapper.emitted('removed')).toHaveLength(1)
    expect(wrapper.find('[data-removed]').exists()).toBe(true)
    expect(wrapper.findComponent(KnownPlayersAccountsPanel).exists()).toBe(false)

    api.getPersona.mockResolvedValue(detail({ mergedIntoSlug: 'peng' }))
    const merged = mount(KnownPlayersEditor, { global: editorGlobal, props: { personaId: 'p2' } })
    await flushPromises()
    expect(merged.text()).toContain('Merged into peng')
    expect(merged.find('button[data-rename]').exists()).toBe(false)
  })

  it('says when the player cannot be loaded', async () => {
    api.getPersona.mockRejectedValue(new Error('boom'))
    const wrapper = mount(KnownPlayersEditor, { global: editorGlobal, props: { personaId: 'p1' } })
    await flushPromises()
    expect(wrapper.text()).toContain('Failed to load this known player')
  })
})

describe('KnownPlayersCreate', () => {
  it('suggests the slug from the name and creates the player with its first account and link', async () => {
    api.createPersona.mockResolvedValue({ id: 'p9', slug: 'g2-caps', accounts: [] })
    const wrapper = mount(KnownPlayersCreate, { global })
    const inputs = () => wrapper.findAll('input')
    await inputs()[0]!.setValue('G2 Caps')
    expect((inputs()[1]!.element as HTMLInputElement).value).toBe('g2-caps')
    await inputs()[2]!.setValue('G2 Caps#1323')
    await inputs()[3]!.setValue('https://www.twitch.tv/caps')
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(api.createPersona).toHaveBeenCalledWith({
      slug: 'g2-caps',
      displayName: 'G2 Caps',
      accounts: [{ gameName: 'G2 Caps', tagLine: '1323', platformId: 'EUW1' }],
      links: [{ kind: 'twitch', url: 'https://www.twitch.tv/caps' }],
    })
    expect(wrapper.emitted('created')![0]![0]).toMatchObject({ id: 'p9' })
  })

  it('keeps an edited slug, refuses a bad one, and shows the API\'s refusal', async () => {
    api.createPersona.mockRejectedValue({ data: { detail: 'The slug \'caps\' is already used by a known player, live, merged or removed.' } })
    const wrapper = mount(KnownPlayersCreate, { global })
    const inputs = () => wrapper.findAll('input')
    await inputs()[1]!.setValue('Bad Slug')
    await inputs()[0]!.setValue('Caps')
    expect((inputs()[1]!.element as HTMLInputElement).value).toBe('Bad Slug')
    expect(wrapper.find('[data-field="Slug"] [data-error]').exists()).toBe(true)
    await wrapper.find('form').trigger('submit')
    expect(api.createPersona).not.toHaveBeenCalled()

    await inputs()[1]!.setValue('caps')
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(wrapper.find('[data-failure]').text()).toContain('already used')
    await wrapper.findAll('button').find(b => b.text() === 'Cancel')!.trigger('click')
    expect(wrapper.emitted('cancel')).toHaveLength(1)
  })
})

describe('KnownPlayersDirectory', () => {
  it('lists the players with the filters, opens one, and adds one', async () => {
    const refresh = vi.fn()
    const query = ref<Record<string, unknown>>({})
    const replace = vi.fn(({ query: next }: { query: Record<string, unknown> }) => {
      query.value = next
    })
    const usePersonas = vi.fn(() => ({
      data: ref({ personas: [row(), row({ id: 'p2', slug: 'faker', displayName: 'Faker' })], total: 30, page: 1, pageSize: 25 }),
      pending: ref(false),
      error: ref(null),
      refresh,
    }))
    vi.stubGlobal('usePersonas', usePersonas)
    vi.stubGlobal('useRoute', () => ({ get query() { return query.value } }))
    vi.stubGlobal('useRouter', () => ({ replace }))
    vi.stubGlobal('refDebounced', (source: unknown) => source)
    vi.stubGlobal('ALL', 'all')
    api.getPersona.mockResolvedValue(detail())

    const wrapper = mount(KnownPlayersDirectory, {
      global: { components: { ...editorGlobal.components, KnownPlayersList, KnownPlayersEditor, KnownPlayersCreate } },
    })
    await flushPromises()
    expect(wrapper.text()).toContain('30 players')
    expect(wrapper.findAll('[data-row]')).toHaveLength(2)
    expect(wrapper.find('nav').exists()).toBe(true)

    const filters = (usePersonas.mock.calls[0] as unknown as [{ value: Record<string, unknown> }])[0]
    expect(filters.value).toMatchObject({ view: undefined, source: undefined, page: 1, pageSize: 25 })
    await wrapper.find('select[aria-label="Which known players"]').setValue('Conflicts')
    expect(filters.value).toMatchObject({ view: 'Conflicts' })

    await wrapper.findAll('[data-row]')[1]!.trigger('click')
    await flushPromises()
    expect(replace).toHaveBeenCalledWith({ query: { player: 'p2' } })
    expect(wrapper.findComponent(KnownPlayersEditor).props('personaId')).toBe('p2')
    wrapper.findComponent(KnownPlayersEditor).vm.$emit('changed')
    expect(refresh).toHaveBeenCalledTimes(1)

    wrapper.findComponent(KnownPlayersCreate).vm.$emit('created', {
      id: 'p9', slug: 'caps-new', accounts: [{ gameName: 'A', tagLine: 'B', platformId: 'EUW1', status: 'seeded' }],
    })
    await flushPromises()
    expect(toast.success).toHaveBeenCalledWith('caps-new added', expect.stringContaining('Account added'))
    expect(wrapper.findComponent(KnownPlayersEditor).props('personaId')).toBe('p9')
  })
})
