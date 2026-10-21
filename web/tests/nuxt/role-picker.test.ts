import { mountSuspended } from '@nuxt/test-utils/runtime'
import { describe, expect, it } from 'vitest'
import { RolePicker } from '#components'

// The position strip: one position (the filters), or several at once (the
// desktop kit's positions, #2205).
describe('RolePicker', () => {
  it('picks one position, or all of them', async () => {
    const wrapper = await mountSuspended(RolePicker, { props: { position: 'TOP' } })
    expect(wrapper.find('[aria-label="All positions"]').exists()).toBe(true)
    await wrapper.get('[aria-label="Middle"]').trigger('click')
    await wrapper.get('[aria-label="All positions"]').trigger('click')
    expect(wrapper.emitted('update:position')).toEqual([['MIDDLE'], [null]])
    expect(wrapper.get('[aria-label="Top"]').attributes('aria-pressed')).toBeUndefined()
  })

  it('toggles several positions, kept in lane order, with no "All" button', async () => {
    const wrapper = await mountSuspended(RolePicker, { props: { positions: ['MIDDLE'] } })
    expect(wrapper.find('[aria-label="All positions"]').exists()).toBe(false)
    expect(wrapper.get('[aria-label="Middle"]').attributes('aria-pressed')).toBe('true')
    expect(wrapper.get('[aria-label="Top"]').attributes('aria-pressed')).toBe('false')
    await wrapper.get('[aria-label="Top"]').trigger('click')
    await wrapper.get('[aria-label="Middle"]').trigger('click')
    expect(wrapper.emitted('update:positions')).toEqual([[['TOP', 'MIDDLE']], [[]]])
  })
})
