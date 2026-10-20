import { mountSuspended } from '@nuxt/test-utils/runtime'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { MatchDetailTimelineEventAxis } from '#components'
import type { WinProbabilityTimeline } from '#shared/types/win-probability'
import { axisEvents } from '#common/utils/timeline-events'

// The match detail's event axis, after the desktop recap's timeline: a dot an
// event, ours above the line and theirs below, merged when closer than a dot's
// width at the rendered size, and a tooltip listing a dot's events.

const participants = [
  { participantId: 1, teamId: 100, position: 'TOP' },
  { participantId: 2, teamId: 100, position: 'JUNGLE' },
  { participantId: 6, teamId: 200, position: 'TOP' },
  { participantId: 7, teamId: 200, position: 'JUNGLE' },
]
const timeline: WinProbabilityTimeline = {
  durationMs: 1_000_000,
  participants,
  frames: [],
  events: [
    { type: 'CHAMPION_KILL', ms: 100_000, killerId: 1, victimId: 6 },
    { type: 'CHAMPION_KILL', ms: 105_000, killerId: 1, victimId: 7 },
    { type: 'ELITE_MONSTER_KILL', ms: 110_000, killerId: 2, killerTeamId: 100, monsterType: 'DRAGON', monsterSubType: 'FIRE_DRAGON' },
    { type: 'CHAMPION_KILL', ms: 108_000, killerId: 6, victimId: 2 },
    { type: 'CHAMPION_KILL', ms: 500_000, killerId: 0, victimId: 1 },
    { type: 'ELITE_MONSTER_KILL', ms: 900_000, killerId: 7, killerTeamId: 200, monsterType: 'BARON_NASHOR' },
  ],
}
const NAMES: Record<number, string> = { 1: 'Ahri', 2: 'Lee Sin', 6: 'Darius', 7: 'Vi' }
const champion = (id: number) => ({
  name: NAMES[id]!,
  iconUrl: id === 7 ? null : `https://ddragon.leagueoflegends.com/cdn/16.20.1/img/champion/${NAMES[id]}.png`,
})

/** The track, measured `width` px wide: at 1000 px a dot's 10 px is 10 s of this 1 000 s game. */
function mountAxis(width: number) {
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({
    left: 0, top: 0, width, height: 28, right: width, bottom: 28, x: 0, y: 0, toJSON: () => ({}),
  } as DOMRect)
  return mountSuspended(MatchDetailTimelineEventAxis, {
    props: { events: axisEvents(timeline, 100), durationMs: timeline.durationMs, minutes: [{ minute: 5, x: 30 }], champion },
  })
}

afterEach(() => vi.restoreAllMocks())

describe('MatchDetailTimelineEventAxis', () => {
  it('draws our dots above the line in blue and theirs below in red, merged at the rendered width', async () => {
    const wrapper = await mountAxis(1000)
    const dots = wrapper.findAll('button')
    // Ours: the two kills and the drake as one dot; theirs: the kill, the execution, the Baron.
    expect(dots.map(dot => [dot.classes().includes('bg-ally') ? 'ours' : 'theirs', dot.attributes('style')])).toEqual([
      ['ours', 'left: 10.5%;'],
      ['theirs', 'left: 10.8%;'],
      ['theirs', 'left: 50%;'],
      ['theirs', 'left: 90%;'],
    ])
    expect(dots[0]!.classes()).toEqual(expect.arrayContaining(['top-[7px]', 'size-3']))
    expect(dots[1]!.classes()).toEqual(expect.arrayContaining(['top-[21px]', 'size-2.5', 'bg-enemy']))
    expect(wrapper.text()).toContain('5\'')
  })

  it('keeps every dot apart when the track is wide enough', async () => {
    const wrapper = await mountAxis(10_000)
    expect(wrapper.findAll('button.bg-ally')).toHaveLength(3)
  })

  it('opens the hovered dot\'s tooltip above the line: a double kill in portraits, the drake\'s minimap icon', async () => {
    const wrapper = await mountAxis(1000)
    const track = wrapper.find('.relative.h-7')
    await track.trigger('pointermove', { clientX: 104, clientY: 5 })
    const tooltip = wrapper.find('[role="tooltip"]')
    expect(tooltip.classes()).toContain('bottom-full')
    expect(tooltip.text()).toContain('Double kill')
    expect(tooltip.text()).toContain('Your Infernal Drake')
    expect(tooltip.text()).toContain('1:40')
    const images = tooltip.findAll('img')
    expect(images.map(img => img.attributes('alt'))).toEqual(['Ahri', 'Darius', ''])
    expect(images[2]!.attributes('src')).toContain('dragon_infernal')
    // Vi has no icon: an empty ringed slot named after her.
    expect(tooltip.find('span[title="Vi"]').exists()).toBe(true)
    await track.trigger('pointerleave')
    expect(wrapper.find('[role="tooltip"]').exists()).toBe(false)
  })

  it('opens theirs below the line, an execution with its skull, and on keyboard focus', async () => {
    const wrapper = await mountAxis(1000)
    const track = wrapper.find('.relative.h-7')
    await track.trigger('pointermove', { clientX: 500, clientY: 25 })
    const tooltip = wrapper.find('[role="tooltip"]')
    expect(tooltip.classes()).toContain('top-full')
    expect(tooltip.text()).toContain('Executed')
    expect(tooltip.find('span.text-enemy').exists()).toBe(true)
    await track.trigger('pointerleave')

    const baron = wrapper.findAll('button').at(-1)!
    expect(baron.attributes('aria-label')).toBe('Enemy Baron at 15:00')
    await baron.trigger('focus')
    expect(wrapper.find('[role="tooltip"]').text()).toContain('Enemy Baron')
    expect(wrapper.find('[role="tooltip"]').attributes('style')).toContain('calc(-100% + 12px)')
    await baron.trigger('blur')
    expect(wrapper.find('[role="tooltip"]').exists()).toBe(false)
  })
})
