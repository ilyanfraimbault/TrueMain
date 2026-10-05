import { beforeEach, describe, expect, it } from 'vitest'
import type { PlayerGame } from '~/types/record'
import type { Goal } from '~/utils/goals'
import { METRICS } from '~/utils/player-form'
import { averageOf, evaluate, goalSentence, suggestGoals } from '~/utils/goals'
import { readGoals, saveGoals } from '~/utils/goal-store'
import { MINUTE, game } from './fixtures'

const T = Date.UTC(2026, 9, 5, 12)
const RIOT_ID = 'Player#EUW'

function goal(overrides: Partial<Goal> = {}): Goal {
  return {
    id: 'g1',
    riotId: RIOT_ID,
    metric: 'cs',
    comparator: 'atLeast',
    threshold: 7,
    games: 3,
    mode: 'average',
    hitsNeeded: 3,
    scope: { queue: 'solo' },
    createdAt: T,
    status: 'active',
    results: [],
    ...overrides,
  }
}

/** Thirty-minute games an hour apart from `T`, newest first, as the client lists them. */
function played(...games: Partial<PlayerGame>[]): PlayerGame[] {
  return games
    .map((overrides, index) => game({ gameId: index + 1, playedAt: T + (index + 1) * 60 * MINUTE, ...overrides }))
    .reverse()
}

describe('evaluate', () => {
  it('counts only games in scope, oldest first', () => {
    const games = played(
      { cs: 240 },
      { cs: 300, queueId: 440 },
      { cs: 210, championId: 2 },
      { cs: 180, position: 'TOP' },
    )
    expect(evaluate(goal(), games).results.map(result => result.gameId)).toEqual([1, 3, 4])
    const ahriMid = goal({ scope: { queue: 'solo', championId: 1, position: 'MIDDLE' } })
    expect(evaluate(ahriMid, games).results.map(result => result.gameId)).toEqual([1])
    expect(evaluate(goal({ scope: { queue: 'all' } }), games).counted).toBe(3)
  })

  it('leaves remakes out', () => {
    const games = played({ cs: 240 }, { cs: 0, remake: true }, { cs: 240 })
    expect(evaluate(goal(), games).results.map(result => result.gameId)).toEqual([1, 3])
  })

  it('never counts a game that started before the goal was set', () => {
    const before = game({ gameId: 99, playedAt: T - MINUTE, cs: 300 })
    const evaluation = evaluate(goal(), [...played({ cs: 240 }), before])
    expect(evaluation.results.map(result => result.gameId)).toEqual([1])
  })

  it('extends the run past a game the metric cannot measure', () => {
    const kp = goal({ metric: 'kp', threshold: 0.5, mode: 'eachGame', games: 2, hitsNeeded: 2 })
    const games = played({ teamKills: 20 }, { teamKills: null }, { teamKills: 20 })
    const evaluation = evaluate(kp, games)
    expect(evaluation.results.map(result => result.gameId)).toEqual([1, 3])
    expect(evaluation.status).toBe('done')
  })

  it('averages as a ratio of sums, the way the tiles do', () => {
    const games = played({ cs: 300, durationSeconds: 3600 }, { cs: 120, durationSeconds: 1200 }, { cs: 210 })
    const evaluation = evaluate(goal(), games)
    expect(evaluation.value).toBeCloseTo(630 / 110)
    for (const metric of METRICS) {
      const each = evaluate(goal({ metric: metric.key, games: 10 }), games)
      expect(averageOf(metric.key, each.results)).toBeCloseTo(metric.over(games)!)
    }
  })

  it('decides an average goal once its N games are counted', () => {
    expect(evaluate(goal(), played({ cs: 240 }, { cs: 240 })).status).toBe('active')
    expect(evaluate(goal(), played({ cs: 240 }, { cs: 240 }, { cs: 210 })).status).toBe('done')
    expect(evaluate(goal(), played({ cs: 240 }, { cs: 150 }, { cs: 150 })).status).toBe('missed')
  })

  it('reads the value the player sees: 6.96 CS / min shows 7.0 and meets 7', () => {
    expect(evaluate(goal({ games: 1 }), played({ cs: 208.8 })).status).toBe('done')
  })

  it('misses an each-game goal as soon as K can no longer be reached', () => {
    const deaths = goal({ metric: 'deaths', comparator: 'atMost', threshold: 5, mode: 'eachGame', games: 5, hitsNeeded: 4 })
    const games = played({ deaths: 3 }, { deaths: 7 }, { deaths: 8 }, { deaths: 2 })
    const evaluation = evaluate(deaths, games)
    expect(evaluation.status).toBe('missed')
    expect(evaluation.results.map(result => result.gameId)).toEqual([1, 2, 3])
  })

  it('stays decided from its frozen results once its games leave the history', () => {
    const decided = evaluate(goal(), played({ cs: 240 }, { cs: 240 }, { cs: 210 }))
    const frozen = goal({ results: decided.results, status: decided.status })
    expect(evaluate(frozen, [])).toMatchObject({ status: 'done', counted: 3, value: decided.value })
  })

  it('keeps counting after the frozen games, never one twice', () => {
    const games = played({ cs: 240 }, { cs: 240 }, { cs: 210 })
    const first = evaluate(goal(), games.slice(2))
    const resumed = evaluate(goal({ results: first.results }), games)
    expect(resumed.results.map(result => result.gameId)).toEqual([1, 2, 3])
  })

  it('waits for older pages when the read does not reach back to where the run left off', () => {
    const games = played({ cs: 240 }, { cs: 240 }, { cs: 210 }).slice(0, 2)
    const evaluation = evaluate(goal(), games, true)
    expect(evaluation).toMatchObject({ needsOlder: true, counted: 0, status: 'active' })
    expect(evaluate(goal(), games, false).counted).toBe(2)
  })

  it('leaves an abandoned goal as it is', () => {
    expect(evaluate(goal({ status: 'abandoned' }), played({ cs: 240 }))).toMatchObject({ status: 'abandoned', counted: 0 })
  })
})

describe('goal storage', () => {
  beforeEach(() => localStorage.clear())

  it('keeps each account its own goals', () => {
    saveGoals(RIOT_ID, [goal()])
    expect(readGoals(RIOT_ID)).toHaveLength(1)
    expect(readGoals('Other#EUW')).toEqual([])
    // A goal of another account handed in by mistake is never written under this one.
    saveGoals('Other#EUW', [goal()])
    expect(readGoals('Other#EUW')).toEqual([])
  })

  it('reads a broken store as no goals', () => {
    localStorage.setItem(`truemain:goals:v1:${RIOT_ID}`, '{nope')
    expect(readGoals(RIOT_ID)).toEqual([])
  })

  it('keeps every active goal and the latest twenty finished ones', () => {
    const finished = Array.from({ length: 25 }, (_, index) => goal({ id: `f${index}`, status: 'done', createdAt: T + index }))
    const kept = saveGoals(RIOT_ID, [goal({ id: 'a', createdAt: T - 1 }), ...finished])
    expect(kept).toHaveLength(21)
    expect(kept[0]!.id).toBe('a')
    expect(kept.some(entry => entry.id === 'f0')).toBe(false)
  })
})

describe('suggestGoals', () => {
  /** Ten Solo games newest first: the latest five worse than the five before on CS, vision and deaths. */
  function slump(position: string): PlayerGame[] {
    return Array.from({ length: 10 }, (_, index) => {
      const recent = index < 5
      // Deaths move KDA too: kills and assists rise with them so it stays flat.
      const deaths = recent ? 8 : 4
      return game({ gameId: index + 1, position, cs: recent ? 150 : 240, visionScore: recent ? 20 : 40, deaths, kills: deaths, assists: deaths })
    })
  }

  it('suggests the weakest metrics at the player\'s own average', () => {
    const suggestions = suggestGoals(slump('MIDDLE'), [])
    expect(suggestions.map(entry => entry.metric)).toEqual(['vision', 'deaths', 'cs'])
    const cs = suggestions.find(entry => entry.metric === 'cs')
    expect(cs).toMatchObject({ comparator: 'atLeast', threshold: 6.5, mode: 'average', scope: { queue: 'solo' } })
    expect(suggestions.find(entry => entry.metric === 'deaths')).toMatchObject({ comparator: 'atMost', mode: 'eachGame' })
  })

  it('never suggests CS to a support, and puts vision first', () => {
    const suggestions = suggestGoals(slump('UTILITY'), [])
    expect(suggestions.map(entry => entry.metric)).not.toContain('cs')
    expect(suggestions[0]!.metric).toBe('vision')
  })

  it('suggests nothing on a short sample, nor on a metric an active goal tracks', () => {
    expect(suggestGoals(slump('MIDDLE').slice(0, 7), [])).toEqual([])
    const suggestions = suggestGoals(slump('MIDDLE'), [goal({ metric: 'cs' })])
    expect(suggestions.map(entry => entry.metric)).not.toContain('cs')
    expect(suggestions.length).toBeLessThanOrEqual(2)
  })

  it('measures on Solo when the dashboard reads every queue', () => {
    const aram = slump('MIDDLE').map(entry => ({ ...entry, queueId: 450 }))
    expect(suggestGoals(aram, [], 'all')).toEqual([])
  })
})

describe('goalSentence', () => {
  it('writes the goal out in full', () => {
    expect(goalSentence(goal())).toBe('CS / min ≥ 7.0 over the next 3 Solo games')
    const deaths = goal({ metric: 'deaths', comparator: 'atMost', threshold: 5, mode: 'eachGame', games: 5, hitsNeeded: 5 })
    expect(goalSentence(deaths)).toBe('Deaths ≤ 5 in each of the next 5 Solo games')
    expect(goalSentence({ ...deaths, hitsNeeded: 4, scope: { queue: 'solo', position: 'MIDDLE' } }, 'Ahri'))
      .toBe('Deaths ≤ 5 in 4 of the next 5 Solo games · Ahri · Mid')
    expect(goalSentence(goal({ metric: 'kp', threshold: 0.5, scope: { queue: 'all' } }))).toBe('Kill part. ≥ 50% over the next 3 games')
  })
})
