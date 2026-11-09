import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed } from 'vue'
import DashboardChampionsCard from '../app/components/dashboard/DashboardChampionsCard.vue'

// The champions card outside Nuxt: the site's list stubbed to render each main's slots and
// its link, so the test reads the share, the record and the lane the card computes.
const global = {
  stubs: {
    ProfileMainChampions: {
      props: ['mains', 'linkTo'],
      template: `<ul><li v-for="main in mains" :key="main.championId" :data-play-rate="main.playRate"
        :data-link="JSON.stringify(linkTo(main))"><slot name="meta" :main="main" /> | <slot name="value" :main="main" /></li></ul>`,
    },
  },
}

beforeEach(() => {
  vi.stubGlobal('computed', computed)
  vi.stubGlobal('useChampionSlugs', () => ({ pathFor: (id: number) => `/champions/${id}` }))
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('DashboardChampionsCard', () => {
  it('shows each champion\'s share, record and win rate, and opens it on the lane it is played', () => {
    const wrapper = mount(DashboardChampionsCard, {
      props: {
        champions: [],
        lines: [
          { championId: 103, games: 3, wins: 2, kills: 6, deaths: 3, assists: 9, position: 'MIDDLE' },
          { championId: 7, games: 1, wins: 0, kills: 0, deaths: 2, assists: 1, position: null },
        ],
      },
      global,
    })

    const [ahri, leblanc] = wrapper.findAll('li')
    expect(ahri!.attributes('data-play-rate')).toBe('0.75')
    expect(ahri!.text()).toContain('3 games')
    expect(ahri!.text()).toContain('2W 1L')
    expect(JSON.parse(ahri!.attributes('data-link')!)).toEqual({ path: '/champions/103', query: { position: 'MIDDLE' } })
    expect(leblanc!.text()).toContain('1 game ·')
    expect(JSON.parse(leblanc!.attributes('data-link')!)).toEqual({ path: '/champions/7', query: {} })
  })
})
