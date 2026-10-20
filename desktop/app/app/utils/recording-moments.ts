import type { DragonKind, Moment, MomentKind, RecordingWinProbability } from '~/types/recordings'
import { formatDuration } from '#common/utils/relativeTime'

/**
 * How a recording's moments read: their names and colours on the
 * timeline, and the title a clip proposes from the moments inside it.
 *
 * The colours are the app's own vocabulary: a kill is the data axis' good end
 * (rose gold), a death the red the match rows give deaths, an assist the
 * neutral ramp — and an objective is coloured by the side that took it, in the
 * draft's ally/enemy convention (`--color-ally`, `--color-enemy`).
 */

/**
 * Which way a moment went, the colour of its dot on the timeline: the player's
 * kills and assists and their side's objectives are the ally's, their deaths
 * and the enemy's objectives the enemy's.
 */
export const momentSide = (moment: Moment): 'ally' | 'enemy' =>
  moment.kind === 'death' || moment.ally === false ? 'enemy' : 'ally'

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

const DRAKE_NAMES: Record<DragonKind, string> = {
  infernal: 'Infernal Drake',
  mountain: 'Mountain Drake',
  ocean: 'Ocean Drake',
  cloud: 'Cloud Drake',
  hextech: 'Hextech Drake',
  chemtech: 'Chemtech Drake',
}

/** "Triple kill", "Death", "Baron", "Enemy Infernal Drake" ("Enemy Dragon" when the element was not kept). */
export function momentLabel(moment: Moment): string {
  switch (moment.kind) {
    case 'kill': return MULTI_KILLS[Math.min(Math.max(moment.kills, 1), 5) - 1]!
    case 'death': return 'Death'
    case 'assist': return 'Assist'
    default: {
      const name = (moment.kind === 'dragon' && moment.dragon && DRAKE_NAMES[moment.dragon]) || OBJECTIVE_NAMES[moment.kind]
      return moment.ally === false ? `Enemy ${name}` : name
    }
  }
}

/** The game's own minimap icons, one per drake element and per objective. */
const MINIMAP_ICONS = 'https://raw.communitydragon.org/latest/game/assets/ux/minimap/icons'

const OBJECTIVE_ICONS: Record<Exclude<MomentKind, 'kill' | 'death' | 'assist'>, string> = {
  dragon: 'dragon',
  elder: 'dragon_elder',
  baron: 'baron',
  herald: 'riftherald',
  grubs: 'grub',
  atakhan: 'atakhan_r',
  tower: 'tower',
  inhibitor: 'inhibitor',
}

/** An objective's icon as the game's minimap draws it; `null` for the player's kills, deaths and assists. */
export function objectiveIconUrl(moment: Moment): string | null {
  if (moment.kind === 'kill' || moment.kind === 'death' || moment.kind === 'assist') return null
  const file = moment.kind === 'dragon' && moment.dragon ? `dragon_${moment.dragon}` : OBJECTIVE_ICONS[moment.kind]
  return `${MINIMAP_ICONS}/${file}.png`
}

/**
 * Who played which champion in the recorded game: the player's own, from the
 * recording, and every participant's by id, from the scoreboard kept beside
 * it — empty when it has none (a clip, a recording without its curve).
 */
export interface MomentCast {
  playerChampionId: number | null
  champions: ReadonlyMap<number, number>
}

/** The cast from the recording's champion and, when it was kept, the game's scoreboard. */
export function momentCast(playerChampionId: number | null, scoreboard: Pick<RecordingWinProbability, 'champions'> | null): MomentCast {
  return {
    playerChampionId,
    champions: new Map((scoreboard?.champions ?? []).map(entry => [entry.participantId, entry.championId])),
  }
}

/** The champions on either side of a kill, death or assist: who landed it, who fell. */
export interface MomentDuel {
  /** `null` for an execution (a tower, minions, a monster) or an unknown player. */
  killer: number | null
  victims: number[]
}

/**
 * The kill's two sides in champions: the player is the killer of their own
 * kills and the victim of their deaths. `null` for an objective, and when
 * nothing on either side can be named.
 */
export function momentDuel(moment: Moment, cast: MomentCast): MomentDuel | null {
  const champion = (participant: number | null) => (participant === null ? null : cast.champions.get(participant) ?? null)
  const victimsOf = () => moment.victimIds.map(champion).filter((id): id is number => id !== null)
  let duel: MomentDuel
  switch (moment.kind) {
    case 'kill': duel = { killer: cast.playerChampionId, victims: victimsOf() }; break
    case 'death': duel = { killer: champion(moment.killerId), victims: cast.playerChampionId ? [cast.playerChampionId] : [] }; break
    case 'assist': duel = { killer: champion(moment.killerId), victims: victimsOf() }; break
    default: return null
  }
  return duel.killer === null && !duel.victims.length ? null : duel
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

/** Footage time in milliseconds: `m:ss`, or `h:mm:ss` past the hour. */
export function formatClock(ms: number): string {
  return formatDuration(ms / 1000, { hours: true })
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
