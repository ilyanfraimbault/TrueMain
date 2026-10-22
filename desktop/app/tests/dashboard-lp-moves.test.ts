import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import DashboardLpMoves from '../app/components/dashboard/DashboardLpMoves.vue'

describe('DashboardLpMoves', () => {
  it('lists each move signed, coloured on the data axis, in the order given', () => {
    const wrapper = mount(DashboardLpMoves, { props: { moves: [{ gameId: 1, delta: -15 }, { gameId: 2, delta: 23 }] } })
    const chips = wrapper.findAll('li')
    expect(chips.map(chip => chip.text())).toEqual(['−15', '+23'])
    expect(chips[0]!.classes()).toContain('text-data-bad')
    expect(chips[1]!.classes()).toContain('text-data-good')
    expect(wrapper.text()).toContain('Last 2 games')
  })

  it('names a single game in the singular', () => {
    expect(mount(DashboardLpMoves, { props: { moves: [{ gameId: 1, delta: 20 }] } }).text()).toContain('Last game')
  })

  it('renders nothing without a measured game', () => {
    expect(mount(DashboardLpMoves, { props: { moves: [] } }).find('[data-testid="lp-moves"]').exists()).toBe(false)
  })
})
