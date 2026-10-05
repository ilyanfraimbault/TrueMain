import type {
  MatchWinProbability,
  WinProbabilityObjective,
  WinProbabilitySwing,
  WinProbabilitySwingKind,
  WinProbabilityTimeline,
  WinProbabilityTimelineEvent,
} from '#shared/types/win-probability'
import {
  BARON_BUFF_SECONDS,
  ELDER_BUFF_SECONDS,
  INHIBITOR_RESPAWN_SECONDS,
  WIN_PROBABILITY_LANES,
  winProbability,
  type SideMap,
  type WinProbabilityLane,
} from './win-probability'

/**
 * A finished game's win-probability curve and the moments that swung it
 * (#1911), from its timeline and the shared model (`win-probability.ts`).
 * The ingestor runs the same steps in C# (`Core/Lol/WinProbability`); the
 * fixture `web/shared/fixtures/win-probability-timeline.json` keeps the two
 * identical — change one, change both.
 *
 * - The curve reads team 100's chance at every frame of the timeline (one a
 *   minute) and at the end of the game.
 * - A turning point is a kill, a turret, an inhibitor, a drake, the Elder or
 *   the Baron, weighed at its own time: the chance just after it minus just
 *   before, only that event changing. Creep score and level are interpolated
 *   between the frames around it, and kills and objectives are steps, so that
 *   delta is exact under the model.
 * - The other epic monsters (Rift Herald, Voidgrubs, Atakhan) are listed as
 *   objectives with no delta: the model does not weigh them.
 * - No curve for a game under fifteen minutes (a remake or a surrender before
 *   the lanes mean anything), nor without the five lanes paired across sides.
 */

/** Below this, no curve. */
export const WIN_PROBABILITY_MIN_DURATION_MS = 15 * 60_000
/** How many turning points are kept, largest first. */
export const WIN_PROBABILITY_SWING_LIMIT = 10

const BLUE = 100
const RED = 200

interface LanePair { lane: WinProbabilityLane, blue: number, red: number }

interface MapState {
  turrets: number
  /** The game time (ms) each enemy inhibitor this side destroyed stands again. */
  inhibitors: number[]
  dragons: number
  baronUntilMs: number | null
  elderUntilMs: number | null
}

const emptySide = (): MapState => ({ turrets: 0, inhibitors: [], dragons: 0, baronUntilMs: null, elderUntilMs: null })

/** The five lanes, each a blue and a red player — or null if any lane lacks exactly one of each. */
function lanePairs(timeline: WinProbabilityTimeline): LanePair[] | null {
  const pairs: LanePair[] = []
  for (const lane of WIN_PROBABILITY_LANES) {
    const blue = timeline.participants.filter(p => p.teamId === BLUE && p.position === lane)
    const red = timeline.participants.filter(p => p.teamId === RED && p.position === lane)
    if (blue.length !== 1 || red.length !== 1) return null
    pairs.push({ lane, blue: blue[0]!.participantId, red: red[0]!.participantId })
  }
  return pairs
}

/** The side an event is for, or null when the timeline does not say. */
function sideOf(event: WinProbabilityTimelineEvent, teams: Map<number, number>): number | null {
  if (event.type === 'BUILDING_KILL') {
    // The building's own side lost it.
    if (event.teamId === BLUE) return RED
    if (event.teamId === RED) return BLUE
    return null
  }
  if (event.type === 'ELITE_MONSTER_KILL' && (event.killerTeamId === BLUE || event.killerTeamId === RED)) {
    return event.killerTeamId
  }
  return teams.get(event.killerId ?? 0) ?? null
}

/** The turning-point kind of an event, or null when the model does not weigh it. */
function kindOf(event: WinProbabilityTimelineEvent): WinProbabilitySwingKind | null {
  switch (event.type) {
    case 'CHAMPION_KILL':
      return (event.killerId ?? 0) > 0 ? 'kill' : null
    case 'BUILDING_KILL':
      if (event.buildingType === 'TOWER_BUILDING') return 'turret'
      if (event.buildingType === 'INHIBITOR_BUILDING') return 'inhibitor'
      return null
    case 'ELITE_MONSTER_KILL':
      if (event.monsterType === 'DRAGON') return event.monsterSubType === 'ELDER_DRAGON' ? 'elder' : 'dragon'
      if (event.monsterType === 'BARON_NASHOR') return 'baron'
      return null
    default:
      return null
  }
}

class GameState {
  readonly kills = new Map<number, number>()
  readonly sides = new Map<number, MapState>([[BLUE, emptySide()], [RED, emptySide()]])

  constructor(
    private readonly timeline: WinProbabilityTimeline,
    private readonly pairs: LanePair[],
    private readonly teams: Map<number, number>,
  ) {}

  apply(event: WinProbabilityTimelineEvent) {
    const kind = kindOf(event)
    if (kind === 'kill') {
      const killer = event.killerId!
      this.kills.set(killer, (this.kills.get(killer) ?? 0) + 1)
      return
    }
    const sideId = sideOf(event, this.teams)
    if (kind === null || sideId === null) return
    const side = this.sides.get(sideId)!
    switch (kind) {
      case 'turret': side.turrets++; break
      case 'inhibitor': side.inhibitors.push(event.ms + INHIBITOR_RESPAWN_SECONDS * 1000); break
      case 'dragon': side.dragons++; break
      case 'elder': side.elderUntilMs = event.ms + ELDER_BUFF_SECONDS * 1000; break
      case 'baron': side.baronUntilMs = event.ms + BARON_BUFF_SECONDS * 1000; break
    }
  }

  /** Team 100's chance at `ms`, with the events applied so far. */
  probability(ms: number): number {
    const leads = this.pairs.map(({ lane, blue, red }) => ({
      lane,
      cs: this.frameValue(blue, ms, 'cs') - this.frameValue(red, ms, 'cs'),
      level: this.frameValue(blue, ms, 'level') - this.frameValue(red, ms, 'level'),
      kills: (this.kills.get(blue) ?? 0) - (this.kills.get(red) ?? 0),
    }))
    return winProbability({ leads, ours: this.sideMap(BLUE, ms), theirs: this.sideMap(RED, ms), clock: ms / 1000 })
  }

  private sideMap(sideId: number, ms: number): SideMap {
    const side = this.sides.get(sideId)!
    return {
      turrets: side.turrets,
      inhibitorsDown: side.inhibitors.filter(respawn => ms < respawn).length,
      dragons: side.dragons,
      baron: side.baronUntilMs !== null && ms < side.baronUntilMs,
      elder: side.elderUntilMs !== null && ms < side.elderUntilMs,
    }
  }

  /** A player's creep score or level at `ms`: linear between the frames around it, the nearest outside them. */
  private frameValue(participantId: number, ms: number, field: 'cs' | 'level'): number {
    const frames = this.timeline.frames
    const read = (index: number) => frames[index]!.players.find(p => p.participantId === participantId)?.[field] ?? 0
    if (ms <= frames[0]!.ms) return read(0)
    const last = frames.length - 1
    if (ms >= frames[last]!.ms) return read(last)
    const upper = frames.findIndex(frame => frame.ms >= ms)
    const from = frames[upper - 1]!
    const to = frames[upper]!
    return read(upper - 1) + (read(upper) - read(upper - 1)) * (ms - from.ms) / (to.ms - from.ms)
  }
}

/** The game's curve and turning points, read for team 100; null when the game gets none (see above). */
export function buildWinProbability(timeline: WinProbabilityTimeline): MatchWinProbability | null {
  if (timeline.durationMs < WIN_PROBABILITY_MIN_DURATION_MS || timeline.frames.length === 0) return null
  const pairs = lanePairs(timeline)
  if (!pairs) return null

  const teams = new Map(timeline.participants.map(p => [p.participantId, p.teamId]))
  const frames = [...timeline.frames].sort((a, b) => a.ms - b.ms)
  const ordered: WinProbabilityTimeline = { ...timeline, frames }
  // Stable: events at the same millisecond keep the timeline's order.
  const events = timeline.events
    .filter(event => event.type === 'CHAMPION_KILL' || event.type === 'BUILDING_KILL' || event.type === 'ELITE_MONSTER_KILL')
    .sort((a, b) => a.ms - b.ms)

  const times = frames.map(frame => frame.ms).filter(ms => ms <= timeline.durationMs)
  if (times[times.length - 1] !== timeline.durationMs) times.push(timeline.durationMs)
  const curve = new GameState(ordered, pairs, teams)
  let next = 0
  const points = times.map((ms) => {
    while (next < events.length && events[next]!.ms <= ms) curve.apply(events[next++]!)
    return { ms, p: curve.probability(ms) }
  })

  const state = new GameState(ordered, pairs, teams)
  const swings: WinProbabilitySwing[] = []
  const objectives: WinProbabilityObjective[] = []
  for (const event of events) {
    const kind = kindOf(event)
    const before = state.probability(event.ms)
    state.apply(event)
    const delta = state.probability(event.ms) - before
    const sideId = event.type === 'CHAMPION_KILL' ? (teams.get(event.killerId ?? 0) ?? null) : sideOf(event, teams)

    if (event.type === 'ELITE_MONSTER_KILL' && sideId !== null) {
      objectives.push({
        ms: event.ms,
        monsterType: event.monsterType ?? '',
        monsterSubType: event.monsterSubType || null,
        teamId: sideId,
        delta: kind === null ? null : delta,
      })
    }
    if (kind === null || sideId === null || delta === 0) continue
    swings.push({
      ms: event.ms,
      kind,
      teamId: sideId,
      delta,
      killerId: kind === 'kill' ? event.killerId ?? 0 : 0,
      victimId: kind === 'kill' ? event.victimId ?? 0 : 0,
      assists: kind === 'kill' ? event.assistIds?.length ?? 0 : 0,
      bounty: kind === 'kill' ? event.bounty ?? null : null,
      lane: event.type === 'BUILDING_KILL' ? event.laneType || null : null,
      towerType: kind === 'turret' ? event.towerType || null : null,
      monsterSubType: kind === 'dragon' ? event.monsterSubType || null : null,
    })
  }

  swings.sort((a, b) => Math.abs(b.delta) - Math.abs(a.delta) || a.ms - b.ms)
  return { points, swings: swings.slice(0, WIN_PROBABILITY_SWING_LIMIT), objectives }
}
