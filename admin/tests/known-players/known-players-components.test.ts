import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { defineComponent, h } from 'vue'
import KnownPlayersConflictActions from '~~/app/components/known-players/KnownPlayersConflictActions.vue'
import KnownPlayersConflictList from '~~/app/components/known-players/KnownPlayersConflictList.vue'
import KnownPlayersPersonaCard from '~~/app/components/known-players/KnownPlayersPersonaCard.vue'
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
    expect(rows[0]!.text()).toContain('Leaguepedia · 1 account')
    expect(rows[0]!.text()).not.toContain('1 accounts')
    expect(rows[0]!.text()).toContain('2×')
    expect(rows[0]!.find('mark').text()).toBe('Open')
    expect(rows[1]!.text()).toContain('now Peng#EUW')
    expect(rows[2]!.text()).toContain('not tracked')
    expect(rows[3]!.find('mark').text()).toBe('Merged')
    expect(wrapper.find('[data-empty]').exists()).toBe(false)

    await rows[1]!.trigger('click')
    expect(wrapper.emitted('open')![0]![0]).toMatchObject({ id: 'c2' })
  })

  it('says why the list is empty, but not while it loads', () => {
    const empty = mount(KnownPlayersConflictList, { global, props: { conflicts: [], pending: false, emptyText: 'No open conflict.' } })
    expect(empty.find('[data-empty]').text()).toBe('No open conflict.')
    const loading = mount(KnownPlayersConflictList, { global, props: { conflicts: [], pending: true, emptyText: 'No open conflict.' } })
    expect(loading.find('[data-empty]').exists()).toBe(false)
  })
})

describe('KnownPlayersPersonaCard', () => {
  it('lists accounts with the disputed one marked, and links with their source', () => {
    const wrapper = mount(KnownPlayersPersonaCard, { global, props: { persona: persona(), side: 'Holds it' } })
    expect(wrapper.text()).toContain('Holds it')
    expect(wrapper.text()).toContain('Accounts · 2 (1 tracked)')
    const disputed = wrapper.findAll('[data-disputed]')
    expect(disputed).toHaveLength(1)
    expect(disputed[0]!.text()).toContain('twtv Peng04#NMIXX')
    expect(wrapper.text()).toContain('Leaguepedia · untracked')
    expect(wrapper.find('a').attributes('href')).toBe('https://www.twitch.tv/peng')
  })

  it('says when a player has no account or link', () => {
    const wrapper = mount(KnownPlayersPersonaCard, { global, props: { persona: persona({ accounts: [], links: [] }), side: 'Claims it' } })
    expect(wrapper.text()).toContain('No account.')
    expect(wrapper.text()).toContain('No link.')
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
