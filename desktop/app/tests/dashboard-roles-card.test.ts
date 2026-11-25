import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed } from 'vue'
import DashboardRolesCard from '../app/components/dashboard/DashboardRolesCard.vue'

// The roles card outside Nuxt: the site's breakdown stubbed to render each role's slot,
// so the test reads the share and the win rate the card computes from the client's games.
const global = {
  stubs: {
    ProfilePositionBreakdown: {
      props: ['positions'],
      template: '<ul><li v-for="role in positions" :key="role.position" :data-share="role.rate"><slot name="games" :role="role" /></li></ul>',
    },
  },
}

beforeEach(() => {
  vi.stubGlobal('computed', computed)
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('DashboardRolesCard', () => {
  it('shows each role\'s share of the games and its win rate', () => {
    const wrapper = mount(DashboardRolesCard, {
      props: { lanes: [{ position: 'MIDDLE', games: 3, wins: 2 }, { position: 'TOP', games: 1, wins: 0 }] },
      global,
    })

    const roles = wrapper.findAll('li')
    expect(roles.map(role => role.attributes('data-share'))).toEqual(['0.75', '0.25'])
    expect(roles[0]!.text()).toContain('3 games')
    expect(roles[0]!.text()).toContain('67% WR')
    expect(roles[1]!.text()).toContain('1 game ·')
    expect(roles[1]!.text()).toContain('0% WR')
  })
})
