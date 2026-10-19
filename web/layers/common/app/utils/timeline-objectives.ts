import type { WinProbabilityTimeline, WinProbabilityTimelineEvent } from '#shared/types/win-probability'
import { monsterLabel, structureLabel } from './win-probability-swing'
import { sideOf } from './win-probability-timeline'

/**
 * The objectives under the gold / XP / CS curves (#2102): every epic monster
 * and structure the game's timeline records, by the side that took it — the
 * events the win probability already reads (`win-probability-timeline.ts`).
 * Voidgrubs come one event a grub; a camp's grubs are one mark with a count.
 */

export type ObjectiveMarkerKind = 'dragon' | 'elder' | 'baron' | 'herald' | 'grubs' | 'atakhan' | 'tower' | 'inhibitor'

export interface ObjectiveMarker {
  ms: number
  kind: ObjectiveMarkerKind
  /** The side that took it. */
  teamId: number
  /** From the viewer's side: "Your Infernal Drake", "Lost the outer turret, mid". */
  label: string
  /** Grubs: how many the side took in that camp; 1 otherwise. */
  count: number
}

/** Grub kills closer than this to the side's previous one belong to the same camp. */
const GRUB_CAMP_MS = 90_000

const MONSTER_KINDS: Record<string, ObjectiveMarkerKind> = {
  BARON_NASHOR: 'baron',
  RIFTHERALD: 'herald',
  HORDE: 'grubs',
  ATAKHAN: 'atakhan',
}

function kindOf(event: WinProbabilityTimelineEvent): ObjectiveMarkerKind | null {
  if (event.type === 'BUILDING_KILL') {
    if (event.buildingType === 'TOWER_BUILDING') return 'tower'
    if (event.buildingType === 'INHIBITOR_BUILDING') return 'inhibitor'
    return null
  }
  if (event.type !== 'ELITE_MONSTER_KILL') return null
  if (event.monsterType === 'DRAGON') return event.monsterSubType === 'ELDER_DRAGON' ? 'elder' : 'dragon'
  return MONSTER_KINDS[event.monsterType ?? ''] ?? null
}

function labelOf(event: WinProbabilityTimelineEvent, kind: ObjectiveMarkerKind, ours: boolean, count: number): string {
  if (kind === 'tower' || kind === 'inhibitor') {
    return structureLabel(kind === 'tower' ? 'turret' : 'inhibitor', {
      ours,
      lane: event.laneType || null,
      towerType: event.towerType || null,
    })
  }
  const name = monsterLabel(event.monsterType ?? '', event.monsterSubType || null)
  const times = count > 1 ? ' ×' + count : ''
  return `${ours ? 'Your' : 'Enemy'} ${name}${times}`
}

/** The game's objectives in time order, read for `perspectiveTeamId`. */
export function objectiveMarkers(timeline: WinProbabilityTimeline, perspectiveTeamId: number): ObjectiveMarker[] {
  const teams = new Map(timeline.participants.map(p => [p.participantId, p.teamId]))
  const events = [...timeline.events].sort((a, b) => a.ms - b.ms)
  const markers: (ObjectiveMarker & { event: WinProbabilityTimelineEvent })[] = []

  for (const event of events) {
    const kind = kindOf(event)
    const teamId = kind ? sideOf(event, teams) : null
    if (!kind || teamId === null) continue
    const camp = kind === 'grubs'
      ? markers.findLast(marker => marker.kind === 'grubs' && marker.teamId === teamId)
      : undefined
    if (camp && event.ms - camp.ms <= GRUB_CAMP_MS) {
      camp.count++
      continue
    }
    markers.push({ ms: event.ms, kind, teamId, label: '', count: 1, event })
  }

  return markers.map(({ event, ...marker }) => ({
    ...marker,
    label: labelOf(event, marker.kind, marker.teamId === perspectiveTeamId, marker.count),
  }))
}
