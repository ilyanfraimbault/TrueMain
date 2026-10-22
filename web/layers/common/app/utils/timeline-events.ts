import type { WinProbabilityTimeline } from '#shared/types/win-probability'
import { objectiveMarkers, type ObjectiveMarker } from './timeline-objectives'

/**
 * The game's events under the gold / XP / CS curves, drawn after the desktop
 * recap's timeline (#2171): one dot an event, above the axis for the viewer's
 * side, below it for the enemy's; dots closer than a dot's width at the
 * rendered size share one, and the dot's tooltip lists what it holds — a kill
 * as its killer's champion and its victims', an objective as the game's
 * minimap icon of it (the drake's element).
 */

export type AxisEvent =
  | { type: 'kill', ms: number, ours: boolean, /** `null`: a turret, minions or a monster executed the victim. */ killerId: number | null, victimId: number }
  | { type: 'objective', ms: number, ours: boolean, marker: ObjectiveMarker }

/** Every kill and objective of the game in time order, read for `perspectiveTeamId`. */
export function axisEvents(timeline: WinProbabilityTimeline, perspectiveTeamId: number): AxisEvent[] {
  const teams = new Map(timeline.participants.map(p => [p.participantId, p.teamId]))
  const kills: AxisEvent[] = timeline.events.flatMap((event) => {
    if (event.type !== 'CHAMPION_KILL') return []
    const victimTeam = teams.get(event.victimId ?? 0)
    if (victimTeam === undefined) return []
    const killerId = teams.has(event.killerId ?? 0) ? event.killerId! : null
    // An execution goes the way of the side that did not lose the champion.
    return [{ type: 'kill' as const, ms: event.ms, ours: victimTeam !== perspectiveTeamId, killerId, victimId: event.victimId! }]
  })
  const objectives: AxisEvent[] = objectiveMarkers(timeline, perspectiveTeamId)
    .map(marker => ({ type: 'objective', ms: marker.ms, ours: marker.teamId === perspectiveTeamId, marker }))
  return [...kills, ...objectives].sort((a, b) => a.ms - b.ms)
}

/** How close two dots may sit, px, before they read as one — the recap's `EVENT_CLUSTER_PX`. */
export const AXIS_CLUSTER_PX = 10

/** Events close enough on screen to be one dot, on one side of the axis, drawn at the middle of its first and last. */
export interface AxisCluster {
  ms: number
  ours: boolean
  events: AxisEvent[]
}

/** Each side's events grouped into dots no closer than `gapMs`: a chain of near events is one cluster. */
export function clusterAxisEvents(events: AxisEvent[], gapMs: number): AxisCluster[] {
  const clusters: AxisCluster[] = []
  for (const ours of [true, false]) {
    let current: AxisEvent[] = []
    const close = () => {
      if (current.length) clusters.push({ ms: (current[0]!.ms + current.at(-1)!.ms) / 2, ours, events: current })
      current = []
    }
    for (const event of events.filter(entry => entry.ours === ours).sort((a, b) => a.ms - b.ms)) {
      if (current.length && event.ms - current.at(-1)!.ms > gapMs) close()
      current.push(event)
    }
    close()
  }
  return clusters.sort((a, b) => a.ms - b.ms)
}

/** The cluster on `ours`' side nearest `ms`, when one lies within `toleranceMs` of it. */
export function clusterNear(clusters: AxisCluster[], ours: boolean, ms: number, toleranceMs: number): AxisCluster | null {
  let best: AxisCluster | null = null
  for (const cluster of clusters) {
    const distance = Math.abs(cluster.ms - ms)
    if (cluster.ours === ours && distance <= toleranceMs && (!best || distance < Math.abs(best.ms - ms))) best = cluster
  }
  return best
}

/** A line of a dot's tooltip: a killer's run of kills (a multi-kill is one line), or an objective. */
export type AxisRow =
  | { type: 'kill', ms: number, ours: boolean, killerId: number | null, victimIds: number[], label: string }
  | { type: 'objective', ms: number, ours: boolean, marker: ObjectiveMarker, icon: string }

/** A kill closer than this to the same killer's previous one extends their multi-kill, as the game counts it. */
const MULTI_KILL_MS = 10_000
const MULTI_KILLS = ['Kill', 'Double kill', 'Triple kill', 'Quadra kill', 'Pentakill']

/** A cluster's events as its tooltip lists them, in time order. */
export function axisRows(cluster: AxisCluster): AxisRow[] {
  const rows: AxisRow[] = []
  const lastKill = new Map<number, { row: AxisRow & { type: 'kill' }, ms: number }>()
  for (const event of cluster.events) {
    if (event.type === 'objective') {
      rows.push({ type: 'objective', ms: event.ms, ours: event.ours, marker: event.marker, icon: objectiveIconUrl(event.marker) })
      continue
    }
    const run = event.killerId === null ? undefined : lastKill.get(event.killerId)
    if (run && event.ms - run.ms <= MULTI_KILL_MS) {
      run.row.victimIds.push(event.victimId)
      run.row.label = MULTI_KILLS[Math.min(run.row.victimIds.length, MULTI_KILLS.length) - 1]!
      run.ms = event.ms
      continue
    }
    const row = { type: 'kill' as const, ms: event.ms, ours: event.ours, killerId: event.killerId, victimIds: [event.victimId], label: event.killerId === null ? 'Executed' : 'Kill' }
    rows.push(row)
    if (event.killerId !== null) lastKill.set(event.killerId, { row, ms: event.ms })
  }
  return rows
}

/** The game's own minimap icons, one per drake element and per objective — the recap's. */
const MINIMAP_ICONS = 'https://raw.communitydragon.org/latest/game/assets/ux/minimap/icons'

const OBJECTIVE_ICONS: Record<ObjectiveMarker['kind'], string> = {
  dragon: 'dragon',
  elder: 'dragon_elder',
  baron: 'baron',
  herald: 'riftherald',
  grubs: 'grub',
  atakhan: 'atakhan_r',
  tower: 'tower',
  inhibitor: 'inhibitor',
}

const DRAKE_ICONS: Record<string, string> = {
  FIRE_DRAGON: 'dragon_infernal',
  EARTH_DRAGON: 'dragon_mountain',
  MOUNTAIN_DRAGON: 'dragon_mountain',
  WATER_DRAGON: 'dragon_ocean',
  AIR_DRAGON: 'dragon_cloud',
  HEXTECH_DRAGON: 'dragon_hextech',
  CHEMTECH_DRAGON: 'dragon_chemtech',
}

/** An objective's icon as the game's minimap draws it: the drake's element when the timeline names it. */
export function objectiveIconUrl(marker: Pick<ObjectiveMarker, 'kind' | 'subType'>): string {
  const file = (marker.kind === 'dragon' && marker.subType && DRAKE_ICONS[marker.subType]) || OBJECTIVE_ICONS[marker.kind]
  return `${MINIMAP_ICONS}/${file}.png`
}
