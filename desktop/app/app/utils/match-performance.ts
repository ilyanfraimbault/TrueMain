import type { TimelineMark } from '~/types/record'
import type { LaneLead, PerformanceScoreInput } from '~/utils/performance-score'
import { performanceScore } from '~/utils/performance-score'

/**
 * One game graded the site's way (#2075): every participant scored with
 * `performanceScore` on the inputs the site builds
 * (`Api/Services/Truemains/PlayerChampions/PerformanceInputs.cs`), then ranked
 * into a placement with MVP and ACE (`Core/Lol/Performance/MatchPerformanceRanker.cs`).
 * The shared vectors pin this half too.
 */

/** One participant's scoreboard line and marks (`ScoredParticipant` plus their marks). */
export interface ScoredParticipant {
  participantId: number
  teamId: number
  /** `TOP` … `UTILITY`; null or empty where the game had no roles. */
  teamPosition: string | null
  win: boolean
  kills: number
  deaths: number
  assists: number
  cs: number
  damageToChampions: number
  goldEarned: number
  visionScore: number
  marks: TimelineMark[]
}

/** Where one participant landed (`MatchPerformancePlacement`). */
export interface MatchPerformancePlacement {
  participantId: number
  score: number
  /** 1..10, 1 the best of the game. */
  placement: number
  /** Best of the winning side. */
  isMvp: boolean
  /** Best of the losing side. */
  isAce: boolean
}

/** The site's canonical marks, `TimelineSnapshotMarks.Minutes`. */
const MARK_MINUTES = [5, 10, 15, 20, 30]

/** Every participant's scoring input, side totals and lane leads included (`BuildMatchInputs`). */
export function matchInputs(participants: ScoredParticipant[], durationSeconds: number): PerformanceScoreInput[] {
  const totals = new Map<number, { kills: number, damage: number, gold: number }>()
  for (const p of participants) {
    const side = totals.get(p.teamId) ?? { kills: 0, damage: 0, gold: 0 }
    side.kills += p.kills
    side.damage += p.damageToChampions
    side.gold += p.goldEarned
    totals.set(p.teamId, side)
  }
  const gameDurationMinutes = durationSeconds > 0 ? durationSeconds / 60 : 0

  return participants.map((p) => {
    const side = totals.get(p.teamId)!
    return {
      teamPosition: p.teamPosition ?? '',
      kills: p.kills,
      deaths: p.deaths,
      assists: p.assists,
      teamKills: side.kills,
      damageToChampions: p.damageToChampions,
      teamDamageToChampions: side.damage,
      goldEarned: p.goldEarned,
      teamGoldEarned: side.gold,
      cs: p.cs,
      visionScore: p.visionScore,
      gameDurationMinutes,
      laneLeads: laneLeads(p, laneOpponent(participants, p)),
    }
  })
}

/** The one participant on the other side holding the same role; none when the pairing is ambiguous (`FindLaneOpponent`). */
export function laneOpponent(participants: ScoredParticipant[], self: ScoredParticipant): ScoredParticipant | null {
  if (!self.teamPosition) return null
  const foes = participants.filter(other => other.teamId !== self.teamId && other.teamPosition === self.teamPosition)
  return foes.length === 1 ? foes[0]! : null
}

/** The leads at every mark both sides have (`BuildLaneLeads`). */
function laneLeads(self: ScoredParticipant, foe: ScoredParticipant | null): LaneLead[] {
  if (!foe) return []
  return MARK_MINUTES.flatMap((minute) => {
    const mine = self.marks.find(mark => mark.minute === minute)
    const theirs = foe.marks.find(mark => mark.minute === minute)
    if (!mine || !theirs) return []
    return [{ minute, goldDiff: mine.gold - theirs.gold, csDiff: mine.cs - theirs.cs, xpDiff: mine.xp - theirs.xp }]
  })
}

/**
 * Grades and ranks one game, keyed by participant id. Ordered by score, then
 * takedowns, then fewer deaths, then participant id, so equal scores still get
 * distinct, stable placements (`MatchPerformanceRanker.Rank`).
 */
export function gradeMatch(participants: ScoredParticipant[], durationSeconds: number): Map<number, MatchPerformancePlacement> {
  const inputs = matchInputs(participants, durationSeconds)
  const ordered = participants
    .map((p, i) => ({ p, score: performanceScore(inputs[i]!) }))
    .sort((a, b) => b.score - a.score
      || (b.p.kills + b.p.assists) - (a.p.kills + a.p.assists)
      || a.p.deaths - b.p.deaths
      || a.p.participantId - b.p.participantId)
  const mvp = ordered.find(entry => entry.p.win)?.p.participantId
  const ace = ordered.find(entry => !entry.p.win)?.p.participantId

  return new Map(ordered.map(({ p, score }, i) => [p.participantId, {
    participantId: p.participantId,
    score,
    placement: i + 1,
    isMvp: p.participantId === mvp,
    isAce: p.participantId === ace,
  }]))
}
