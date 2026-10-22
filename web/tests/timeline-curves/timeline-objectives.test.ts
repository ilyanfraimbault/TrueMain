import { describe, expect, it } from 'vitest'
import type { WinProbabilityTimeline } from '#shared/types/win-probability'
import fixture from '#shared/fixtures/win-probability-timeline.json'
import { objectiveMarkers } from '#common/utils/timeline-objectives'

const participants = [
  { participantId: 1, teamId: 100, position: 'TOP' },
  { participantId: 6, teamId: 200, position: 'TOP' },
]

function game(events: WinProbabilityTimeline['events']): WinProbabilityTimeline {
  return { durationMs: 1_800_000, participants, frames: [], events }
}

describe('objectiveMarkers', () => {
  it('marks the monsters and structures by the side that took them, in time order', () => {
    const markers = objectiveMarkers(game([
      { type: 'ELITE_MONSTER_KILL', ms: 900_000, killerId: 6, killerTeamId: 200, monsterType: 'BARON_NASHOR' },
      { type: 'BUILDING_KILL', ms: 600_000, killerId: 1, teamId: 200, buildingType: 'TOWER_BUILDING', laneType: 'MID_LANE', towerType: 'OUTER_TURRET' },
      { type: 'ELITE_MONSTER_KILL', ms: 400_000, killerId: 1, killerTeamId: 100, monsterType: 'DRAGON', monsterSubType: 'FIRE_DRAGON' },
      { type: 'CHAMPION_KILL', ms: 500_000, killerId: 1, victimId: 6 },
      { type: 'BUILDING_KILL', ms: 1_200_000, killerId: 6, teamId: 100, buildingType: 'INHIBITOR_BUILDING', laneType: 'BOT_LANE' },
    ]), 100)
    expect(markers).toEqual([
      { ms: 400_000, kind: 'dragon', teamId: 100, label: 'Your Infernal Drake', count: 1, subType: 'FIRE_DRAGON' },
      { ms: 600_000, kind: 'tower', teamId: 100, label: 'Took the outer turret, mid', count: 1, subType: null },
      { ms: 900_000, kind: 'baron', teamId: 200, label: 'Enemy Baron', count: 1, subType: null },
      { ms: 1_200_000, kind: 'inhibitor', teamId: 200, label: 'Lost the bot inhibitor', count: 1, subType: null },
    ])
  })

  it('reads the elder, the herald and Atakhan, and skips what it cannot place', () => {
    const markers = objectiveMarkers(game([
      { type: 'ELITE_MONSTER_KILL', ms: 1, killerId: 6, monsterType: 'DRAGON', monsterSubType: 'ELDER_DRAGON' },
      { type: 'ELITE_MONSTER_KILL', ms: 2, killerId: 1, monsterType: 'RIFTHERALD' },
      { type: 'ELITE_MONSTER_KILL', ms: 3, killerId: 1, killerTeamId: 100, monsterType: 'ATAKHAN' },
      { type: 'ELITE_MONSTER_KILL', ms: 4, killerId: 0, monsterType: 'BARON_NASHOR' },
      { type: 'ELITE_MONSTER_KILL', ms: 5, killerId: 1, killerTeamId: 100, monsterType: 'SCUTTLE' },
      { type: 'BUILDING_KILL', ms: 6, teamId: 100, buildingType: 'NEXUS_BUILDING' },
    ]), 200)
    expect(markers.map(m => [m.kind, m.label])).toEqual([
      ['elder', 'Your Elder Drake'],
      ['herald', 'Enemy Rift Herald'],
      ['atakhan', 'Enemy Atakhan'],
    ])
  })

  it('folds a camp of Voidgrubs into one mark with its count', () => {
    const grub = (ms: number, teamId: number) => ({ type: 'ELITE_MONSTER_KILL', ms, killerId: teamId === 100 ? 1 : 6, killerTeamId: teamId, monsterType: 'HORDE' })
    const markers = objectiveMarkers(game([
      grub(360_000, 100), grub(365_000, 100), grub(372_000, 200), grub(380_000, 100), grub(600_000, 100),
    ]), 100)
    expect(markers.map(m => [m.ms, m.teamId, m.count, m.label])).toEqual([
      [360_000, 100, 3, 'Your Voidgrubs ×3'],
      [372_000, 200, 1, 'Enemy Voidgrubs'],
      [600_000, 100, 1, 'Your Voidgrubs'],
    ])
  })

  it('marks every objective of the fixture game', () => {
    const markers = objectiveMarkers(fixture.timeline as WinProbabilityTimeline, 100)
    expect(markers.filter(m => m.kind === 'tower')).toHaveLength(11)
    expect(markers.filter(m => m.kind === 'inhibitor')).toHaveLength(2)
    expect(markers.some(m => m.kind === 'baron')).toBe(true)
  })
})
