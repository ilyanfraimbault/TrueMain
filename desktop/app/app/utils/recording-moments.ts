import type { Moment, MomentKind } from '~/types/recordings'

/**
 * How a recording's moments read: their names and colours on the
 * timeline, and the title a clip proposes from the moments inside it.
 *
 * The colours are the app's own vocabulary: a kill is the data axis' good end
 * (rose gold), a death the red the match rows give deaths, an assist the
 * neutral ramp — and an objective is coloured by the side that took it, in the
 * draft's ally/enemy convention (`--color-ally`, `--color-enemy`).
 */

export const PLAYER_KINDS: MomentKind[] = ['kill', 'death', 'assist']

export const isPlayerMoment = (moment: Moment) => PLAYER_KINDS.includes(moment.kind)

const OBJECTIVE_NAMES: Record<Exclude<MomentKind, 'kill' | 'death' | 'assist'>, string> = {
  elder: 'Elder Dragon',
  baron: 'Baron',
  atakhan: 'Atakhan',
  dragon: 'Dragon',
  herald: 'Rift Herald',
  grubs: 'Voidgrubs',
  inhibitor: 'Inhibitor',
  tower: 'Tower',
}

/** Most telling first: the objective a clip is named after when it holds several. */
const OBJECTIVE_RANK: MomentKind[] = ['elder', 'baron', 'atakhan', 'dragon', 'herald', 'grubs', 'inhibitor', 'tower']

const MULTI_KILLS = ['Kill', 'Double kill', 'Triple kill', 'Quadra kill', 'Pentakill']

/** "Triple kill", "Death", "Baron", "Enemy Dragon". */
export function momentLabel(moment: Moment): string {
  switch (moment.kind) {
    case 'kill': return MULTI_KILLS[Math.min(Math.max(moment.kills, 1), 5) - 1]!
    case 'death': return 'Death'
    case 'assist': return 'Assist'
    default: return moment.ally === false ? `Enemy ${OBJECTIVE_NAMES[moment.kind]}` : OBJECTIVE_NAMES[moment.kind]
  }
}

/** Text colour of a moment's mark. */
export function momentTone(moment: Moment): string {
  switch (moment.kind) {
    case 'kill': return 'text-data-good'
    case 'death': return 'text-red-400'
    case 'assist': return 'text-ink-300'
    default: return moment.ally === false ? 'text-enemy' : 'text-ally'
  }
}

/** The lanes of the timeline, top to bottom: what the player did, then what was done to them. */
export const PLAYER_LANES = ['kill', 'assist', 'death'] as const

export type PlayerLane = typeof PLAYER_LANES[number]

export const LANE_NAMES: Record<PlayerLane, string> = { kill: 'Kills', assist: 'Assists', death: 'Deaths' }

/**
 * A player moment's mark in its lane. Each kind has its own lane, so the mark
 * only has to read as itself: a kill a filled rose-gold chip (gold from a
 * triple kill on, the data axis' standout step), an assist a small neutral
 * dot, a death a dark chip ringed in red.
 */
export function momentMark(moment: Moment): string {
  switch (moment.kind) {
    case 'kill': return moment.kills >= 3 ? 'h-4 min-w-4 bg-gold text-ink-950' : 'h-4 min-w-4 bg-data-good text-ink-950'
    case 'death': return 'size-4 bg-red-950 text-red-300 ring-1 ring-red-400/70'
    default: return 'size-2 bg-ink-400'
  }
}

/** Epic monsters are badges above the track; towers and inhibitors, far more frequent, stay bare glyphs. */
export const isStructure = (moment: Moment) => moment.kind === 'tower' || moment.kind === 'inhibitor'

/** `mm:ss`, or `h:mm:ss` past the hour. */
export function formatClock(ms: number): string {
  const total = Math.max(0, Math.floor(ms / 1000))
  const hours = Math.floor(total / 3600)
  const minutes = Math.floor((total % 3600) / 60)
  const seconds = `${total % 60}`.padStart(2, '0')
  return hours ? `${hours}:${`${minutes}`.padStart(2, '0')}:${seconds}` : `${minutes}:${seconds}`
}

/** The moments that happen inside `[startMs, endMs]`, in order. */
export function momentsIn(moments: Moment[], startMs: number, endMs: number): Moment[] {
  return moments.filter(moment => moment.videoMs >= startMs && moment.videoMs <= endMs)
}

/**
 * The title a range proposes from what it holds: the player's kills ("Triple
 * kill", "3 kills"), then the most telling objective their team took
 * ("Baron"), joined as the product owner worded it ("Kill + Dragon"); the
 * champion follows a kill-led title where it reads naturally ("Pentakill on
 * Ahri"). A range with nothing in it is named after its place in the game.
 */
export function proposeClipTitle(moments: Moment[], startMs: number, endMs: number, champion: string | null): string {
  const inside = momentsIn(moments, startMs, endMs)
  const kills = inside.filter(moment => moment.kind === 'kill')
  const killCount = kills.reduce((sum, moment) => sum + Math.max(moment.kills, 1), 0)
  const biggest = kills.reduce((max, moment) => Math.max(max, moment.kills), 0)

  let killPart = ''
  // One kill run is named by its size; several are counted, unless one of them
  // is a triple or more, which is the headline.
  if (kills.length === 1 || biggest >= 3) killPart = MULTI_KILLS[Math.min(Math.max(biggest, 1), 5) - 1]!
  else if (kills.length > 1) killPart = `${killCount} kills`

  const taken = inside.filter(moment => moment.ally === true)
  const objective = OBJECTIVE_RANK.find(kind => taken.some(moment => moment.kind === kind))
  const objectivePart = objective ? OBJECTIVE_NAMES[objective as keyof typeof OBJECTIVE_NAMES] : ''

  if (killPart && objectivePart) return `${killPart} + ${objectivePart}`
  if (killPart) return champion ? `${killPart} on ${champion}` : killPart
  if (objectivePart) return objectivePart

  const assists = inside.filter(moment => moment.kind === 'assist').length
  if (assists) return assists === 1 ? 'Assist' : `${assists} assists`
  if (inside.some(moment => moment.kind === 'death')) return 'Death'
  const lost = inside.find(moment => moment.ally === false)
  if (lost) return momentLabel(lost)
  return champion ? `${champion} at ${formatClock(startMs)}` : `Clip at ${formatClock(startMs)}`
}
