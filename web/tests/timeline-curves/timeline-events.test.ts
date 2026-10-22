import { describe, expect, it } from 'vitest'
import type { WinProbabilityTimeline } from '#shared/types/win-probability'
import fixture from '#shared/fixtures/win-probability-timeline.json'
import { axisEvents, axisRows, clusterAxisEvents, clusterNear, objectiveIconUrl } from '#common/utils/timeline-events'

const participants = [
  { participantId: 1, teamId: 100, position: 'TOP' },
  { participantId: 2, teamId: 100, position: 'JUNGLE' },
  { participantId: 6, teamId: 200, position: 'TOP' },
  { participantId: 7, teamId: 200, position: 'JUNGLE' },
  { participantId: 8, teamId: 200, position: 'MIDDLE' },
]

function game(events: WinProbabilityTimeline['events']): WinProbabilityTimeline {
  return { durationMs: 1_800_000, participants, frames: [], events }
}

describe('axisEvents', () => {
  it('reads kills and objectives in time order, by the side they went', () => {
    const events = axisEvents(game([
      { type: 'ELITE_MONSTER_KILL', ms: 400_000, killerId: 6, killerTeamId: 200, monsterType: 'DRAGON', monsterSubType: 'WATER_DRAGON' },
      { type: 'CHAMPION_KILL', ms: 300_000, killerId: 1, victimId: 6 },
      { type: 'CHAMPION_KILL', ms: 350_000, killerId: 0, victimId: 2 },
      { type: 'CHAMPION_KILL', ms: 360_000, killerId: 6, victimId: 99 },
      { type: 'WARD_PLACED', ms: 10_000 },
    ]), 100)
    expect(events.map(event => [event.type, event.ms, event.ours])).toEqual([
      ['kill', 300_000, true],
      ['kill', 350_000, false],
      ['objective', 400_000, false],
    ])
    expect(events[1]).toMatchObject({ killerId: null, victimId: 2 })
  })

  it('reads every kill and objective of the fixture game', () => {
    const events = axisEvents(fixture.timeline as WinProbabilityTimeline, 100)
    expect(events.filter(event => event.type === 'kill')).toHaveLength(39)
    expect(events.some(event => event.type === 'objective' && event.marker.kind === 'baron')).toBe(true)
  })
})

describe('clusterAxisEvents', () => {
  const events = axisEvents(game([
    { type: 'CHAMPION_KILL', ms: 100_000, killerId: 1, victimId: 6 },
    { type: 'CHAMPION_KILL', ms: 104_000, killerId: 1, victimId: 7 },
    { type: 'CHAMPION_KILL', ms: 108_000, killerId: 6, victimId: 2 },
    { type: 'CHAMPION_KILL', ms: 200_000, killerId: 2, victimId: 8 },
  ]), 100)

  it('merges each side\'s near events into one dot, never across the axis', () => {
    const clusters = clusterAxisEvents(events, 10_000)
    expect(clusters.map(cluster => [cluster.ms, cluster.ours, cluster.events.length])).toEqual([
      [102_000, true, 2],
      [108_000, false, 1],
      [200_000, true, 1],
    ])
  })

  it('keeps every event apart without a gap', () => {
    expect(clusterAxisEvents(events, 0)).toHaveLength(4)
  })

  it('finds the dot nearest the pointer on the asked side only', () => {
    const clusters = clusterAxisEvents(events, 10_000)
    expect(clusterNear(clusters, true, 105_000, 10_000)?.ms).toBe(102_000)
    expect(clusterNear(clusters, false, 105_000, 10_000)?.ms).toBe(108_000)
    expect(clusterNear(clusters, true, 150_000, 10_000)).toBeNull()
  })
})

describe('axisRows', () => {
  it('folds a killer\'s run into a multi-kill and lists executions and objectives', () => {
    const [cluster] = clusterAxisEvents(axisEvents(game([
      { type: 'CHAMPION_KILL', ms: 100_000, killerId: 1, victimId: 6 },
      { type: 'CHAMPION_KILL', ms: 105_000, killerId: 1, victimId: 7 },
      { type: 'CHAMPION_KILL', ms: 112_000, killerId: 1, victimId: 8 },
      { type: 'CHAMPION_KILL', ms: 113_000, killerId: 2, victimId: 6 },
      { type: 'CHAMPION_KILL', ms: 140_000, killerId: 1, victimId: 6 },
      { type: 'CHAMPION_KILL', ms: 141_000, killerId: 0, victimId: 7 },
      { type: 'ELITE_MONSTER_KILL', ms: 142_000, killerId: 2, killerTeamId: 100, monsterType: 'DRAGON', monsterSubType: 'FIRE_DRAGON' },
    ]), 100), 60_000)
    expect(axisRows(cluster!).map(row => (row.type === 'kill' ? [row.label, row.killerId, row.victimIds] : [row.marker.label, row.icon]))).toEqual([
      ['Triple kill', 1, [6, 7, 8]],
      ['Kill', 2, [6]],
      ['Kill', 1, [6]],
      ['Executed', null, [7]],
      ['Your Infernal Drake', 'https://raw.communitydragon.org/latest/game/assets/ux/minimap/icons/dragon_infernal.png'],
    ])
  })
})

describe('objectiveIconUrl', () => {
  it('draws the minimap icon, the drake\'s element when known', () => {
    expect(objectiveIconUrl({ kind: 'dragon', subType: null })).toMatch(/icons\/dragon\.png$/)
    expect(objectiveIconUrl({ kind: 'dragon', subType: 'CHEMTECH_DRAGON' })).toMatch(/dragon_chemtech\.png$/)
    expect(objectiveIconUrl({ kind: 'dragon', subType: 'MOUNTAIN_DRAGON' })).toMatch(/dragon_mountain\.png$/)
    expect(objectiveIconUrl({ kind: 'grubs', subType: null })).toMatch(/grub\.png$/)
  })
})
