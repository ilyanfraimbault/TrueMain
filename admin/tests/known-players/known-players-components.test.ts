import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { defineComponent, h } from 'vue'
import KnownPlayersConflictActions from '~~/app/components/known-players/KnownPlayersConflictActions.vue'
import KnownPlayersConflictList from '~~/app/components/known-players/KnownPlayersConflictList.vue'
import KnownPlayersConflictCompare from '~~/app/components/known-players/KnownPlayersConflictCompare.vue'
import { conflict, persona } from './fixtures'

// Nuxt UI is auto-imported in the app; here stand-ins render the props a test reads.
const stub = (tag: string, text?: (props: Record<string, unknown>) => string) => defineComponent({
  props: ['label', 'color', 'variant', 'loading', 'disabled', 'icon'],
  emits: ['click'],
  setup(props, { slots, emit, attrs }) {
    return () => h(tag, { ...attrs, 'data-stub': tag, 'onClick': () => emit('click') }, [text ? text(props) : '', slots.default?.()])
  },
})

const global = {
  components: {
    UBadge: stub('mark', p => String(p.label ?? '')),
    UButton: stub('button', p => String(p.label ?? '')),
  },
}

describe('KnownPlayersConflictList', () => {
  it('draws one row per conflict with both players, and opens the clicked one', async () => {
    const renamed = conflict({ id: 'c2', account: { ...conflict().account, currentGameName: 'Peng', currentTagLine: 'EUW' } })
    const untracked = conflict({ id: 'c3', account: { ...conflict().account, riotAccountId: null, currentGameName: null, currentTagLine: null } })
    const resolved = conflict({ id: 'c4', status: 'Resolved', resolution: 'Merged' })
    const wrapper = mount(KnownPlayersConflictList, {
      global,
      props: { conflicts: [conflict(), renamed, untracked, resolved], pending: false, emptyText: 'Nothing.' },
    })

    const rows = wrapper.findAll('[data-row]')
    expect(rows).toHaveLength(4)
    expect(rows[0]!.text()).toContain('twtv Peng04#NMIXX')
    expect(rows[0]!.text()).toContain('Peng (dpm)')
    expect(rows[0]!.text()).toContain('peng-dpm')
    expect(rows[0]!.text()).toContain('Dpm · 4 accounts')
    expect(rows[0]!.get('[data-evidence]').text()).toBe('nothing in common')
    expect(rows[0]!.text()).toContain('Leaguepedia · 1 account')
    expect(rows[0]!.text()).not.toContain('1 accounts')
    expect(rows[0]!.text()).toContain('2×')
    expect(rows[0]!.findAll('mark').at(-1)!.text()).toBe('Open')
    expect(rows[1]!.text()).toContain('now Peng#EUW')
    expect(rows[2]!.text()).toContain('not tracked')
    expect(rows[3]!.findAll('mark').at(-1)!.text()).toBe('Merged')
    expect(wrapper.find('[data-empty]').exists()).toBe(false)

    await rows[1]!.trigger('click')
    expect(wrapper.emitted('open')![0]![0]).toMatchObject({ id: 'c2' })

    // Reachable without a mouse: focusable, opened with Enter or Space.
    expect(rows[2]!.attributes('tabindex')).toBe('0')
    await rows[2]!.trigger('keydown', { key: 'Enter' })
    await rows[3]!.trigger('keydown', { key: ' ' })
    expect(wrapper.emitted('open')!.map(([c]) => (c as { id: string }).id)).toEqual(['c2', 'c3', 'c4'])
  })

  it('says why the list is empty, but not while it loads', () => {
    const empty = mount(KnownPlayersConflictList, { global, props: { conflicts: [], pending: false, emptyText: 'No open conflict.' } })
    expect(empty.find('[data-empty]').text()).toBe('No open conflict.')
    const loading = mount(KnownPlayersConflictList, { global, props: { conflicts: [], pending: true, emptyText: 'No open conflict.' } })
    expect(loading.find('[data-empty]').exists()).toBe(false)
  })
})

describe('KnownPlayersConflictCompare', () => {
  const peng = persona()
  const pengDpm = persona({
    id: 'p2',
    slug: 'peng-dpm',
    displayName: 'Peng (dpm)',
    source: 'Dpm',
    accounts: [{ gameName: 'peng old', tagLine: 'euw', platformId: 'EUW1', source: 'Dpm', riotAccountId: null, disputed: false }],
    links: [
      { kind: 'twitch', url: 'https://twitch.tv/peng/', source: 'Dpm' },
      { kind: 'x', url: 'https://x.com/peng_dpm', source: 'Dpm' },
    ],
  })

  it('lines the two players up: the disputed account first, the shared ones and links on one row', () => {
    const wrapper = mount(KnownPlayersConflictCompare, {
      global: { components: { ...global.components, UIcon: stub('i') } },
      props: { claimant: pengDpm, owner: peng },
    })
    expect(wrapper.text()).toContain('Claims it')
    expect(wrapper.text()).toContain('Holds it')

    const accounts = wrapper.findAll('[data-account-row]')
    expect(accounts).toHaveLength(2)
    expect(accounts[0]!.text()).toContain('claims this account')
    expect(accounts[0]!.text()).toContain('twtv Peng04#NMIXX')
    expect(accounts[1]!.attributes('data-match')).toBe('same')

    const links = wrapper.findAll('[data-link-row]')
    expect(links.map(l => l.attributes('data-match'))).toEqual(['same', 'only'])
    expect(links[0]!.findAll('a').map(a => a.attributes('href'))).toEqual(['https://twitch.tv/peng/', 'https://www.twitch.tv/peng'])
    expect(wrapper.get('[data-verdict]').text()).toBe('1 of 2 links identical · 1 other account in common')
  })

  it('says when neither side has an account or a link', () => {
    const bare = persona({ accounts: [], links: [] })
    const wrapper = mount(KnownPlayersConflictCompare, {
      global: { components: { ...global.components, UIcon: stub('i') } },
      props: { claimant: bare, owner: { ...bare, id: 'p2' } },
    })
    expect(wrapper.text()).toContain('No account on either side.')
    expect(wrapper.text()).toContain('No link on either side.')
    expect(wrapper.get('[data-verdict]').text()).toBe('Neither lists a link to compare.')
  })
})

describe('KnownPlayersConflictActions', () => {
  const button = (wrapper: ReturnType<typeof mount>, key: string) => wrapper.find(`[data-action="${key}"]`)

  it('sends keep at once', async () => {
    const wrapper = mount(KnownPlayersConflictActions, { global, props: { conflict: conflict(), busy: false } })
    await button(wrapper, 'keep').trigger('click')
    expect(wrapper.emitted('resolve')).toEqual([[{ action: 'keep' }]])
  })

  it('arms a destructive action on the first click and sends it on the second', async () => {
    const wrapper = mount(KnownPlayersConflictActions, { global, props: { conflict: conflict(), busy: false } })
    await button(wrapper, 'merge-owner').trigger('click')
    expect(wrapper.emitted('resolve')).toBeUndefined()
    expect(button(wrapper, 'merge-owner').text()).toBe('Confirm: Merge into Peng')

    // Arming another one disarms the first.
    await button(wrapper, 'detach').trigger('click')
    expect(button(wrapper, 'merge-owner').text()).toBe('Merge into Peng')
    await button(wrapper, 'detach').trigger('click')
    expect(wrapper.emitted('resolve')).toEqual([[{ action: 'detach' }]])
  })

  it('sends nothing while a resolution is in flight', async () => {
    const wrapper = mount(KnownPlayersConflictActions, { global, props: { conflict: conflict(), busy: true } })
    await button(wrapper, 'keep').trigger('click')
    expect(wrapper.emitted('resolve')).toBeUndefined()
  })
})
