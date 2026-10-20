import { describe, expect, it } from 'vitest'
import vectors from '#shared/fixtures/performance-score-vectors.json'
import type { MatchDetailResponse } from '#shared/types/match-detail'
import type { GameParticipant } from '~/types/record'
import { gameScore, gradeDetail, toMatchSummary } from '~/utils/match-summary'
import { game } from './fixtures'

const match = vectors.matches[0]!

/** The vector's first match as the client's record carries it. */
const participants: GameParticipant[] = match.participants.map(p => ({
  ...p,
  championId: p.participantId,
  position: p.teamPosition,
  gameName: null,
  tagLine: null,
  marks: match.marks.filter(mark => mark.participantId === p.participantId),
}))

const expected = (participantId: number) => match.placements.find(p => p.participantId === participantId)!

describe('gameScore', () => {
  it('grades the player among the ten as the site does', () => {
    const played = game({ participantId: 6, teamId: 200, win: false, durationSeconds: match.durationSeconds, participants })
    const { score, placement, isMvp, isAce } = expected(6)
    expect(gameScore(played)).toEqual({ performanceScore: score, placement, isMvp, isAce })
  })

  it('prefers the score TrueMain holds for the game', () => {
    const stored = { performanceScore: 41, placement: 7, isMvp: false, isAce: false }
    expect(gameScore(game({ participants }), stored)).toBe(stored)
  })

  it('grades the client copy when TrueMain has no placement for it', () => {
    const unscored = { performanceScore: 0, placement: 0, isMvp: false, isAce: false }
    expect(gameScore(game({ participantId: 3, durationSeconds: match.durationSeconds, participants }), unscored).placement)
      .toBe(expected(3).placement)
  })

  it('leaves a remake and a game without its scoreboard ungraded', () => {
    expect(gameScore(game({ participants, remake: true })).placement).toBe(0)
    expect(gameScore(game({ participants: [] })).performanceScore).toBe(0)
    expect(gameScore(game({ participantId: 42, participants })).placement).toBe(0)
  })

  it('lands in the match row', () => {
    const row = toMatchSummary(game({ participantId: 3, durationSeconds: match.durationSeconds, participants }), 12)
    expect(row.self).toMatchObject({ lpDelta: 12, performanceScore: expected(3).score, isMvp: expected(3).isMvp })
  })
})

describe('gradeDetail', () => {
  const detail = {
    matchId: '1',
    gameDurationSeconds: match.durationSeconds,
    participants: match.participants.map(p => ({
      ...p,
      totalDamageDealtToChampions: p.damageToChampions,
      performanceScore: 0,
      placement: 0,
      isMvp: false,
      isAce: false,
      marks: match.marks.filter(mark => mark.participantId === p.participantId),
    })),
  } as unknown as MatchDetailResponse

  it('grades the ten players of the opened game, the row\'s figures', () => {
    const graded = gradeDetail(detail)
    for (const p of graded.participants) {
      const { score, placement, isMvp, isAce } = expected(p.participantId)
      expect(p).toMatchObject({ performanceScore: score, placement, isMvp, isAce })
    }
  })

  it('leaves a remake as it came', () => {
    const remake = { ...detail, gameDurationSeconds: 200 }
    expect(gradeDetail(remake)).toBe(remake)
  })
})
