import type { DraftReason } from '~/types/draft'
import { LANE_LABELS } from '~/types/draft'

/**
 * How a draft suggestion's reasons read (#1906). The endpoint returns them as
 * data — kind, champion, delta, games — and this is the one place that turns
 * them into words, so every number on a card is a field of the answer and
 * none is computed here.
 */
export interface ReasonText {
  /** The figure the card leads with: a signed delta, or a rate… */
  headline: string
  /** …and its unit, drawn smaller: `pts` or `%`. */
  unit: string
  /** One short line under the champion. */
  short: string
  /** The whole fact, for the hover. */
  full: string
  tone: 'good' | 'bad' | 'neutral'
}

/** Win-rate points, signed: `+3.1 pts`. */
export function draftPoints(delta: number): string {
  return `${delta >= 0 ? '+' : '−'}${Math.abs(delta * 100).toFixed(1)} pts`
}

/** A delta as the card's headline figure: `+3.1`. */
function signed(delta: number): string {
  return draftPoints(delta).replace(' pts', '')
}

export function draftPercent(rate: number): string {
  return `${(rate * 100).toFixed(rate < 0.1 ? 1 : 0)}%`
}

const games = (count: number) => `${count.toLocaleString('en-US')} game${count === 1 ? '' : 's'}`

/** Below this, the solver's lane guess is said to be one. */
const SURE_OPPONENT = 0.9

function laneLabel(position: string): string {
  return LANE_LABELS[position as keyof typeof LANE_LABELS] ?? position.toLowerCase()
}

function signTone(value: number | null): ReasonText['tone'] {
  if (value === null || value === 0) return 'neutral'
  return value > 0 ? 'good' : 'bad'
}

/**
 * The words for one reason. `position` is our lane (for "the most-played mids").
 * An unknown kind — an API newer than the app — still reads, as its figure only.
 */
export function describeReason(reason: DraftReason, nameOf: (championId: number) => string, position: string): ReasonText {
  const name = reason.championId !== null ? nameOf(reason.championId) : ''
  const delta = reason.delta ?? 0
  const lane = laneLabel(position)

  switch (reason.kind) {
    case 'laneMatchup': {
      const guess = reason.probability !== null && reason.probability < SURE_OPPONENT
      return {
        headline: signed(delta),
        unit: 'pts',
        short: `vs ${name}${guess ? ' (likely)' : ''}`,
        full: `${draftPoints(delta)} vs ${name} over ${games(reason.games)}`
          + (guess ? ` — ${draftPercent(reason.probability!)} sure ${name} plays your lane` : ''),
        tone: signTone(delta),
      }
    }
    case 'blindSafety': {
      const count = reason.count ?? 0
      const of = reason.of ?? 0
      return {
        headline: signed(delta),
        unit: 'pts',
        short: count === 0 ? 'Safe blind' : `Behind ${count}/${of} blind`,
        full: `Blind: ${draftPoints(delta)} on average into the ${lane} opponents still open, `
          + `clearly behind into ${count} of the ${of} most played (${games(reason.games)})`,
        tone: count > 1 ? 'bad' : signTone(delta),
      }
    }
    case 'laneStrength':
      return {
        headline: signed(delta),
        unit: 'pts',
        short: delta >= 0 ? `Strong ${lane}` : `Weak ${lane}`,
        full: `${draftPoints(delta)} over the ${lane} average, ${games(reason.games)}`,
        tone: signTone(delta),
      }
    case 'synergy':
      return {
        headline: signed(delta),
        unit: 'pts',
        short: `With ${name}${reason.tentative ? ' (hover)' : ''}`,
        full: `${draftPoints(delta)} with ${name}${reason.tentative ? ', who is only hovering' : ''} over ${games(reason.games)}`,
        tone: signTone(delta),
      }
    case 'lanePhase': {
      const rate = reason.rate ?? 0.5
      return {
        headline: draftPercent(rate).replace('%', ''),
        unit: '%',
        short: `${rate >= 0.5 ? 'Wins' : 'Loses'} lane vs ${name}`,
        full: `Ahead at 15 minutes in ${draftPercent(rate)} of ${games(reason.games)} decided lanes vs ${name}`,
        tone: signTone(rate - 0.5),
      }
    }
    case 'laneThreat':
      return {
        headline: signed(delta),
        unit: 'pts',
        short: `Into your ${name}`,
        full: `Your ${name}: ${draftPoints(delta)} into it over ${games(reason.games)}`
          + (reason.share !== null ? `, which is played in ${draftPercent(reason.share)} of ${lane} games` : ''),
        tone: 'bad',
      }
    case 'banRate': {
      const rate = reason.rate ?? 0
      return {
        headline: draftPercent(rate).replace('%', ''),
        unit: '%',
        short: `Banned in ${draftPercent(rate)}`,
        full: `Banned in ${draftPercent(rate)} of ${games(reason.games)} this patch — the lane's most banned, not a threat measured against you`,
        tone: 'neutral',
      }
    }
    default:
      return {
        headline: reason.delta !== null ? signed(delta) : reason.rate !== null ? draftPercent(reason.rate).replace('%', '') : '',
        unit: reason.delta !== null ? 'pts' : reason.rate !== null ? '%' : '',
        short: name,
        full: name,
        tone: signTone(reason.delta),
      }
  }
}
