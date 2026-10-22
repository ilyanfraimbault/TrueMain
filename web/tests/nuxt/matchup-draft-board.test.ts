import { mountSuspended } from '@nuxt/test-utils/runtime'
import { describe, expect, it } from 'vitest'
import { BuilderDraftBoard, BuilderLaneDuel } from '#components'
import type { CompositionLane } from '~~/shared/types/composition'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import type { ChampionPosition } from '#common/utils/positions'
import { championLoadingUrlFromIcon } from '~~/shared/utils/ddragon'

/**
 * The matchup page's draft board, drawn like the desktop app's champion select:
 * which two cards are the matchup, which slot a card's click fills, and what the
 * lane duel between the teams reads.
 */

const champions: ChampionStaticListItem[] = [
  { championId: 266, name: 'Aatrox', iconUrl: 'https://ddragon.leagueoflegends.com/cdn/16.20.1/img/champion/Aatrox.png' },
  { championId: 777, name: 'Yone', iconUrl: 'https://ddragon.leagueoflegends.com/cdn/16.20.1/img/champion/Yone.png' },
  { championId: 64, name: 'Lee Sin', iconUrl: 'https://ddragon.leagueoflegends.com/cdn/16.20.1/img/champion/LeeSin.png' },
]

function slots(picks: Partial<Record<ChampionPosition, number>> = {}): Record<ChampionPosition, number | null> {
  return { TOP: null, JUNGLE: null, MIDDLE: null, BOTTOM: null, UTILITY: null, ...picks }
}

function boardProps(overrides: Record<string, unknown> = {}) {
  return {
    champions,
    playedChampionId: 266,
    playedPosition: 'TOP' as ChampionPosition | null,
    opponentChampionId: 777,
    allySlots: slots({ JUNGLE: 64 }),
    enemySlots: slots(),
    hasPicks: true,
    lane: null,
    laneLoading: false,
    ...overrides,
  }
}

describe('BuilderDraftBoard', () => {
  it('rings the played role as the matchup on both sides, the other lanes as draft slots', async () => {
    const wrapper = await mountSuspended(BuilderDraftBoard, { props: boardProps() })
    expect(wrapper.find('[aria-label="Your champion, top — Aatrox"]').exists()).toBe(true)
    expect(wrapper.find('[aria-label="Role opponent, top — Yone"]').exists()).toBe(true)
    expect(wrapper.find('[aria-label="Ally jungle — Lee Sin"]').exists()).toBe(true)
    expect(wrapper.find('[aria-label="Enemy middle — pick a champion"]').exists()).toBe(true)
    // Eight draft slots and the two matchup cards: five a side.
    expect(wrapper.findAll('li button')).toHaveLength(10)
    expect(wrapper.html()).toContain('/img/champion/loading/Aatrox_0.jpg')
  })

  it('fills the slot of the card clicked', async () => {
    const wrapper = await mountSuspended(BuilderDraftBoard, { props: boardProps() })
    await wrapper.get('[aria-label="Enemy middle — pick a champion"]').trigger('click')
    const search = wrapper.findComponent({ name: 'BuilderChampionSearch' })
    expect(search.props('title')).toBe('Choose the enemy middle')
    expect(search.props('championId')).toBeNull()
    search.vm.$emit('select', 64)
    expect(wrapper.emitted('update:enemy')).toEqual([['MIDDLE', 64]])

    await wrapper.get('[aria-label="Ally jungle — Lee Sin"]').trigger('click')
    expect(search.props('championId')).toBe(64)
    search.vm.$emit('select', null)
    expect(wrapper.emitted('update:ally')).toEqual([['JUNGLE', null]])

    await wrapper.get('[aria-label="Role opponent, top — Yone"]').trigger('click')
    search.vm.$emit('select', 266)
    expect(wrapper.emitted('update:opponentChampionId')).toEqual([[266]])
  })

  it('takes a card\'s lane as the role while none is picked', async () => {
    const wrapper = await mountSuspended(BuilderDraftBoard, {
      props: boardProps({ playedPosition: null, playedChampionId: null, opponentChampionId: null, allySlots: slots(), hasPicks: false }),
    })
    expect(wrapper.text()).toContain('Pick your role')
    expect(wrapper.text()).not.toContain('Clear draft')
    await wrapper.get('[aria-label="Enemy bottom — pick a champion"]').trigger('click')
    expect(wrapper.emitted('update:playedPosition')).toEqual([['BOTTOM']])
    const search = wrapper.findComponent({ name: 'BuilderChampionSearch' })
    expect(search.props('title')).toBe('Choose the role opponent')
    search.vm.$emit('select', 777)
    expect(wrapper.emitted('update:opponentChampionId')).toEqual([[777]])
  })
})

describe('BuilderLaneDuel', () => {
  const lane: CompositionLane = { measuredGames: 40, decidedGames: 33, winRate: 0.55, averageGoldDiffAt15: 169, averageXpDiffAt15: 318 }
  const [aatrox, yone] = champions

  it('reads the lane over the sample once the three picks are in', async () => {
    const wrapper = await mountSuspended(BuilderLaneDuel, { props: { position: 'TOP', champion: aatrox!, opponent: yone!, lane, loading: false } })
    expect(wrapper.text()).toContain('55%')
    expect(wrapper.text()).toContain('Lane win rate')
    expect(wrapper.text()).toContain('of 33 decided')
    expect(wrapper.text()).toContain('+169 gold')
  })

  it('names the jungle\'s figure as a matchup tempo, and says what is missing', async () => {
    const jungle = await mountSuspended(BuilderLaneDuel, { props: { position: 'JUNGLE', champion: aatrox!, opponent: yone!, lane: { ...lane, winRate: null }, loading: false } })
    expect(jungle.text()).toContain('Ahead at 15')
    expect(jungle.text()).toContain('nothing decided yet')

    const waiting = await mountSuspended(BuilderLaneDuel, { props: { position: 'TOP', champion: aatrox!, opponent: null, lane: null, loading: false } })
    expect(waiting.text()).toContain('Pick the role opponent')
    await waiting.get('[aria-label="Choose the role opponent"]').trigger('click')
    expect(waiting.emitted('pick-opponent')).toHaveLength(1)
  })
})

describe('championLoadingUrlFromIcon', () => {
  it('maps a Data Dragon icon to its loading-screen art', () => {
    expect(championLoadingUrlFromIcon(champions[2]!.iconUrl)).toBe('https://ddragon.leagueoflegends.com/cdn/img/champion/loading/LeeSin_0.jpg')
    expect(championLoadingUrlFromIcon('/not-an-icon.webp')).toBeNull()
    expect(championLoadingUrlFromIcon(null)).toBeNull()
  })
})
