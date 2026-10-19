import { mountSuspended } from '@nuxt/test-utils/runtime'
import { describe, expect, it } from 'vitest'
import { MatchDetailPanel, MatchDetailTimelineCurves } from '#components'
import type { MatchDetailParticipant, MatchDetailResponse } from '#shared/types/match-detail'
import type { RuneTreeResponse } from '#shared/types/static-data'
import type { WinProbabilityTimeline } from '#shared/types/win-probability'
import fixture from '#shared/fixtures/win-probability-timeline.json'
import { MATCH_DETAIL_SOURCE } from '#common/utils/match-detail-source'

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
    const row = () => wrapper.findAll('button').find(button => button.attributes('aria-label')?.includes('Champion 131'))!
    await row().trigger('click')
    expect(row().attributes('aria-pressed')).toBe('true')
    const pinned = wrapper.findAll('polyline').filter(line => line.attributes('stroke-width') === '2' && line.classes().includes('stroke-enemy'))
    expect(pinned).toHaveLength(1)
    await row().trigger('click')
    expect(row().attributes('aria-pressed')).toBe('false')
  })

  it('marks the objectives on the time axis', async () => {
    const wrapper = await mountCurves()
    expect(wrapper.findAll('[title*="turret"]').length).toBeGreaterThan(0)
    expect(wrapper.find('[title*="Baron"]').exists()).toBe(true)
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
