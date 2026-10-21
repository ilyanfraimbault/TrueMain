import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed } from 'vue'
import DraftSuggestionCard from '../app/components/draft/DraftSuggestionCard.vue'
import type { DraftReason } from '~/types/draft'
import type { PlayerGame } from '~/types/record'
import { comfortOf, comfortText, COMFORT_GAMES } from '~/utils/draft-comfort'
import { describeReason } from '~/utils/draft-reasons'
import { winRateTone } from '#common/utils/rate-tone'

/**
 * The player's comfort on a suggested pick (#1906): counted from their own
 * recent games, said on the card's hover beside the measured reasons, never
 * part of the order.
 */

const game = (championId: number, win: boolean, remake = false) => ({ championId, win, remake }) as PlayerGame

describe('comfortOf', () => {
  it('counts the champion over the latest games, wins apart', () => {
    const games = [game(1, true), game(2, false), game(1, false), game(1, true), game(3, true)]
    expect(comfortOf(games, 1)).toEqual({ played: 3, of: 5, wins: 2 })
  })

  it('leaves remakes out and stops at the first page of history', () => {
    const games = [game(1, false, true), ...Array.from({ length: COMFORT_GAMES }, () => game(2, true)), game(1, true)]
    expect(comfortOf(games, 1)).toBeNull()
    expect(comfortOf(games, 2)).toEqual({ played: COMFORT_GAMES, of: COMFORT_GAMES, wins: COMFORT_GAMES })
  })

  it('reads a sentence with the numbers it was given', () => {
    expect(comfortText('Annie', { played: 12, of: 20, wins: 7 })).toBe('Your Annie: 12 of your last 20 games, 7 wins')
    expect(comfortText('Annie', { played: 1, of: 20, wins: 1 })).toBe('Your Annie: 1 of your last 20 games, 1 win')
  })
})

const global = {
  stubs: {
    ChampionArt: true,
    TierBadge: true,
    UIcon: true,
    UBadge: { template: '<span class="badge"><slot /></span>' },
  },
}

const teamFit: DraftReason = {
  kind: 'teamFit',
  championId: null,
  position: null,
  delta: 0.021,
  games: 1240,
  rate: null,
  probability: null,
  share: null,
  count: null,
  of: null,
  tentative: false,
  axis: 'AllyMagicDamage',
  bucket: 'Low',
}

beforeEach(() => {
  vi.stubGlobal('computed', computed)
  vi.stubGlobal('useChampionStatics', () => ({ nameOf: () => 'Ahri' }))
  vi.stubGlobal('describeReason', describeReason)
  vi.stubGlobal('winRateTone', winRateTone)
})

afterEach(() => vi.unstubAllGlobals())

describe('DraftSuggestionCard', () => {
  const props = { championId: 103, thin: false, position: 'MIDDLE', entry: null, focused: true, height: 1 }

  it('leads with its first reason and says the comfort line on hover', () => {
    const wrapper = mount(DraftSuggestionCard, {
      props: { ...props, reasons: [teamFit], comfort: 'Your Ahri: 12 of your last 20 games, 7 wins' },
      global,
    })

    expect(wrapper.text()).toContain('+2.1')
    expect(wrapper.text()).toContain('Physical team')
    expect(wrapper.attributes('title')).toBe(
      '+2.1 pts with a mostly physical team, over 1,240 games\nYour Ahri: 12 of your last 20 games, 7 wins')
  })

  it('still says the comfort line with no measured reason', () => {
    const wrapper = mount(DraftSuggestionCard, {
      props: { ...props, reasons: [], comfort: 'Your Ahri: 3 of your last 20 games, 1 win' },
      global,
    })

    expect(wrapper.attributes('title')).toBe('Your Ahri: 3 of your last 20 games, 1 win')
  })

  it('marks a thin card and keeps the comfort line after its figures', () => {
    const wrapper = mount(DraftSuggestionCard, {
      props: { ...props, reasons: [teamFit], thin: true, comfort: 'Your Ahri: 2 of your last 20 games, 2 wins' },
      global,
    })

    expect(wrapper.find('.badge').exists()).toBe(true)
    expect(wrapper.attributes('title')?.split('\n')).toEqual([
      '+2.1 pts with a mostly physical team, over 1,240 games',
      'Few games behind these figures',
      'Your Ahri: 2 of your last 20 games, 2 wins',
    ])
  })
})

describe('DraftSuggestions', () => {
  const stubs = {
    ...global.stubs,
    DraftChampionAction: true,
    ChampionPortrait: true,
    UTooltip: { template: '<div><slot /></div>' },
    USwitch: true,
    UInput: true,
  }

  async function mountStrip(mode: 'pick' | 'ban') {
    const { ref } = await import('vue')
    vi.stubGlobal('ref', ref)
    vi.stubGlobal('useChampionStatics', () => ({ nameOf: (id: number) => (id === 103 ? 'Ahri' : 'Zed'), champions: ref(new Map()) }))
    vi.stubGlobal('useChampSelectActions', () => ({ writable: ref(true) }))
    vi.stubGlobal('useTierList', () => ({ entryOf: () => null, status: ref('ready') }))
    vi.stubGlobal('useDamageProfiles', () => ({ profileOf: () => null }))
    vi.stubGlobal('usePlayerRecord', () => ({ games: ref([game(103, true), game(103, false), game(64, true)]) }))
    const { default: DraftSuggestions } = await import('../app/components/draft/DraftSuggestions.vue')
    return mount(DraftSuggestions, {
      props: {
        mode,
        suggestions: [{ championId: 103, reasons: [teamFit], thin: false }, { championId: 238, reasons: [teamFit], thin: false }],
        hasPool: true,
        pending: false,
        position: 'MIDDLE',
        error: null,
        allies: [],
      },
      global: { stubs, components: { DraftSuggestionCard } },
    })
  }

  it('says the player\'s recent games on the picks they have played', async () => {
    const titles = (await mountStrip('pick')).findAll('[title]').map(card => card.attributes('title'))

    expect(titles[0]).toContain('Your Ahri: 2 of your last 3 games, 1 win')
    expect(titles[1]).not.toContain('of your last')
  })

  it('says none on a ban', async () => {
    const titles = (await mountStrip('ban')).findAll('[title]').map(card => card.attributes('title'))

    expect(titles.join('\n')).not.toContain('of your last')
  })
})
