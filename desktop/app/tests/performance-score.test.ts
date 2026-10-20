import { describe, expect, it } from 'vitest'
import vectors from '#shared/fixtures/performance-score-vectors.json'
import type { PerformanceScoreInput } from '~/utils/performance-score'
import { performanceScore } from '~/utils/performance-score'
import type { ScoredParticipant } from '~/utils/match-performance'
import { gradeMatch, laneOpponent, matchInputs } from '~/utils/match-performance'

// The vectors the site's C# scorer runs too (`PerformanceScoreVectorsTests`):
// the port must give every figure the site gives.

describe('performanceScore', () => {
  it.each(vectors.scores.map(v => [v.name, v] as const))('%s', (_, vector) => {
    expect(performanceScore(vector.input as PerformanceScoreInput)).toBe(vector.score)
  })
})

/** A vector's participants, each carrying their own marks as the client sends them. */
function participantsOf(match: (typeof vectors.matches)[number]): ScoredParticipant[] {
  return match.participants.map(p => ({
    ...p,
    marks: match.marks.filter(mark => mark.participantId === p.participantId),
  }))
}

describe('gradeMatch', () => {
  it.each(vectors.matches.map(m => [m.name, m] as const))('%s', (_, match) => {
    const graded = [...gradeMatch(participantsOf(match), match.durationSeconds).values()]
      .sort((a, b) => a.placement - b.placement)
    expect(graded).toEqual(match.placements)
  })

  it('crowns one MVP among the winners and one ACE among the losers', () => {
    const graded = [...gradeMatch(participantsOf(vectors.matches[0]!), 1969).values()]
    expect(graded.filter(p => p.isMvp)).toHaveLength(1)
    expect(graded.filter(p => p.isAce)).toHaveLength(1)
    expect(graded.map(p => p.placement).sort((a, b) => a - b)).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 10])
  })
})

describe('matchInputs', () => {
  const players = participantsOf(vectors.matches[0]!)

  it('sums each side and reads the leads at the marks both lane opponents have', () => {
    const [top] = matchInputs(players, 1969)
    const blue = players.filter(p => p.teamId === 100)
    expect(top!.teamKills).toBe(blue.reduce((sum, p) => sum + p.kills, 0))
    expect(top!.laneLeads.map(lead => lead.minute)).toEqual([5, 10, 15, 20, 30])
    expect(top!.gameDurationMinutes).toBeCloseTo(1969 / 60)
  })

  it('drops the lanes of a pairing that is ambiguous or missing', () => {
    expect(laneOpponent(participantsOf(vectors.matches[3]!), participantsOf(vectors.matches[3]!)[0]!)).toBeNull()
    expect(laneOpponent(players, { ...players[0]!, teamPosition: null })).toBeNull()
    expect(matchInputs([{ ...players[0]!, marks: [] }, players[5]!], 1969)[0]!.laneLeads).toEqual([])
  })

  it('drops every per-minute component of a game without a length', () => {
    expect(matchInputs(players, 0)[0]!.gameDurationMinutes).toBe(0)
  })
})
