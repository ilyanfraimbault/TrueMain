import { mountSuspended } from '@nuxt/test-utils/runtime'
import { describe, expect, it } from 'vitest'
import { MatchDetailPanel, MatchDetailTimelineCurves, MatchDetailTimelineCurvesTooltip } from '#components'
import type { MatchDetailParticipant, MatchDetailResponse } from '#shared/types/match-detail'
import type { RuneTreeResponse } from '#shared/types/static-data'
import type { WinProbabilityTimeline } from '#shared/types/win-probability'
import fixture from '#shared/fixtures/win-probability-timeline.json'
import { MATCH_DETAIL_SOURCE } from '#common/utils/match-detail-source'
import { buildTimelineCurves, teamCurves, type CurveMetric } from '#common/utils/timeline-curves'

// The match detail's gold / XP / CS curves (#2102): ten curves from the
// client's timeline, the viewer's on top, a switch, the lead band, the lists
// whose rows pin a line — and no Timeline tab at all when the game carries no
// timeline, as on the site.

const timeline = fixture.timeline as WinProbabilityTimeline
const CHAMPIONS = [875, 121, 103, 21, 412, 150, 64, 131, 81, 555]
const participants = timeline.participants.map((p, index) => ({
  participantId: p.participantId,
  teamId: p.teamId,
  championId: CHAMPIONS[index]!,
})) as MatchDetailParticipant[]
const champions = CHAMPIONS.map(championId => ({ championId, name: `Champion ${championId}`, iconUrl: '' }))

function mountCurves() {
  return mountSuspended(MatchDetailTimelineCurves, {
    props: { timeline, perspectiveTeamId: 100, selfParticipantId: 3, participants, champions },
  })
}

describe('MatchDetailTimelineCurves', () => {
  it('draws ten curves, the viewer\'s last in the primary colour, and the lead band', async () => {
    const wrapper = await mountCurves()
    const lines = wrapper.findAll('polyline')
    // Ten players, then the band's line.
    expect(lines).toHaveLength(11)
    expect(lines[9]!.classes()).toContain('stroke-primary')
    expect(lines.slice(0, 9).every(line => line.classes().includes('opacity-40'))).toBe(true)
    expect(wrapper.text()).toContain('Gold over the game')
    expect(wrapper.text()).toMatch(/Your team \+\d+\.\dk gold/)
  })

  it('switches the curves, the header and the lists together', async () => {
    const wrapper = await mountCurves()
    const cs = wrapper.findAll('button').find(button => button.text() === 'CS')!
    await cs.trigger('click')
    expect(cs.attributes('aria-pressed')).toBe('true')
    expect(wrapper.text()).toContain('CS over the game')
    expect(wrapper.text()).toMatch(/Your team [+−]\d+ CS/)
    // The fixture's mid laner ends on 247 CS.
    expect(wrapper.text()).toContain('247')
  })

  it('pins a player\'s line from the list, and releases it on a second click', async () => {
    const wrapper = await mountCurves()
    const row = () => wrapper.findAll('button[aria-pressed]').find(button => button.attributes('aria-label')?.includes('Champion 131'))!
    await row().trigger('click')
    expect(row().attributes('aria-pressed')).toBe('true')
    const pinned = wrapper.findAll('polyline').filter(line => line.attributes('stroke-width') === '2' && line.classes().includes('stroke-enemy'))
    expect(pinned).toHaveLength(1)
    await row().trigger('click')
    expect(row().attributes('aria-pressed')).toBe('false')
  })

  it('dots the kills and objectives on the time axis, ours above and theirs below', async () => {
    const wrapper = await mountCurves()
    const dots = wrapper.findAll('button[aria-label*=" at "]')
    expect(dots.some(dot => dot.classes().includes('bg-ally'))).toBe(true)
    expect(dots.some(dot => dot.classes().includes('bg-enemy'))).toBe(true)
    expect(wrapper.find('button[aria-label*="Your Baron"]').exists()).toBe(true)
    expect(wrapper.find('button[aria-label*="Champion 875 kill: Champion 81"]').exists()).toBe(true)
  })
})

describe('MatchDetailTimelineCurves hover', () => {
  it('opens a card with all ten players at the hovered frame', async () => {
    const wrapper = await mountCurves()
    const plot = wrapper.find('.h-40.flex-1')
    plot.element.getBoundingClientRect = () => ({ left: 0, width: 400, top: 0, height: 160, right: 400, bottom: 160, x: 0, y: 0, toJSON: () => ({}) })
    await plot.trigger('pointermove', { clientX: 200 })
    const card = wrapper.findComponent(MatchDetailTimelineCurvesTooltip)
    expect(card.exists()).toBe(true)
    expect(card.findAll('li')).toHaveLength(10)
    expect(card.text()).toMatch(/Your team|Teams even/)
    await wrapper.find('.min-w-0.flex-1').trigger('pointerleave')
    expect(wrapper.findComponent(MatchDetailTimelineCurvesTooltip).exists()).toBe(false)
  })
})

describe('MatchDetailTimelineCurvesTooltip', () => {
  const index = 15
  const gold = teamCurves(buildTimelineCurves(timeline, 'gold', 100)!, 100)

  function mountCard(metric: CurveMetric, x: number, pinnedParticipantId: number | null = null) {
    const teams = teamCurves(buildTimelineCurves(timeline, metric, 100)!, 100)
    const champion = (participantId: number) => ({ name: `Player ${participantId}`, iconUrl: `https://cdn.test/${participantId}.png` })
    return mountSuspended(MatchDetailTimelineCurvesTooltip, {
      props: { x, ms: index * 60_000, lead: 1234, index, teams, metric, pinnedParticipantId, champion },
    })
  }

  it('lists your side then the enemy\'s, highest first, each row a portrait and a gold figure', async () => {
    const wrapper = await mountCard('gold', 30)
    expect(wrapper.text()).toContain('15:00')
    expect(wrapper.text()).toContain('Your team +1.2k gold')
    const columns = wrapper.findAll('ul')
    expect(columns.map(column => column.attributes('aria-label'))).toEqual(['Your team', 'Enemy team'])
    columns.forEach((column, side) => {
      const ids = column.findAll('li').map(row => Number(row.attributes('data-participant')))
      expect(new Set(ids)).toEqual(new Set(gold[side]!.players.map(player => player.participantId)))
      const values = ids.map(id => gold[side]!.players.find(player => player.participantId === id)!.points[index]!.value)
      expect(values).toEqual([...values].sort((a, b) => b - a))
    })
    expect(wrapper.findAll('img')).toHaveLength(10)
    expect(wrapper.findAll('li .tabular-nums').every(value => value.classes().includes('text-gold'))).toBe(true)
  })

  it('leaves the viewer\'s row unmarked and rings the pinned portrait', async () => {
    const wrapper = await mountCard('gold', 30, 7)
    expect(wrapper.find('li[data-participant="3"] .font-semibold').exists()).toBe(false)
    expect(wrapper.find('li[data-participant="7"] .ring-inverted').exists()).toBe(true)
    expect(wrapper.findAll('.ring-inverted')).toHaveLength(1)
  })

  it('prints XP in the plain tone, and opens on the cursor\'s roomier side', async () => {
    const xp = await mountCard('xp', 80)
    expect(xp.findAll('li .tabular-nums').every(value => value.classes().includes('text-highlighted'))).toBe(true)
    expect(xp.find('.text-gold').exists()).toBe(false)
    expect(xp.get('div').attributes('style')).toContain('right: 20%')
    const cs = await mountCard('cs', 30)
    expect(cs.get('div').attributes('style')).toContain('left: 30%')
  })
})

describe('MatchDetailPanel', () => {
  const runeTree = { trees: [] } as unknown as RuneTreeResponse

  function mountPanel(detail: Partial<MatchDetailResponse>) {
    const data = ref<MatchDetailResponse | null>({
      matchId: 'EUW1_1',
      queueId: 420,
      gameMode: 'CLASSIC',
      gameStartTimeUtc: '2026-10-01T00:00:00Z',
      gameDurationSeconds: 1842,
      gameVersion: '16.20.1',
      participants: [],
      ...detail,
    })
    return mountSuspended(MatchDetailPanel, {
      props: { nameTag: 'Synthetic#TAPE', matchId: '1', champions, items: {}, summonerSpells: {}, runeTree },
      global: {
        provide: {
          [MATCH_DETAIL_SOURCE as symbol]: () => ({ data, isLoading: ref(false), notFound: ref(false) }),
        },
      },
    })
  }

  const tabs = (wrapper: Awaited<ReturnType<typeof mountPanel>>) => wrapper.findAll('[role="tab"]').map(tab => tab.text())

  it('has no Timeline tab without the client\'s timeline, as on the site', async () => {
    expect(tabs(await mountPanel({}))).toEqual(['Overview', 'Build', 'Runes'])
  })

  it('opens a Timeline tab when the timeline carries the curves', async () => {
    expect(tabs(await mountPanel({ winProbabilityTimeline: timeline }))).toContain('Timeline')
  })
})
