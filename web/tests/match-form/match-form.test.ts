import { describe, expect, it } from 'vitest'
import type { FormTile } from '#common/utils/match-form'
import { MIN_GAMES_FOR_DELTA, formTiles } from '#common/utils/match-form'
import type { MatchSummaryResponse, MatchSummarySelf } from '~~/shared/types/matches'

/**
 * The four form tiles the profile opens on, on the site and in the app alike.
 *
 * What the tiles must get right is the move, not the average: under
 * `MIN_GAMES_FOR_DELTA` games the newest five are most of the sample, so no move
 * is read at all; a flat run shows none either; and "better" points down for
 * deaths and up for everything else.
 */

/** One thirty-minute game; the self line the tiles read, every other field inert. */
function match(index: number, self: Partial<MatchSummarySelf> = {}, gameDurationSeconds = 1800): MatchSummaryResponse {
  return {
    matchId: `EUW1_${index}`,
    queueId: 420,
    gameMode: 'CLASSIC',
    gameStartTimeUtc: '2026-10-01T12:00:00Z',
    gameDurationSeconds,
    self: {
      championId: 103,
      championLevel: 18,
      summoner1Id: 4,
      summoner2Id: 14,
      primaryStyleId: 8100,
      subStyleId: 8000,
      keystoneId: 8112,
      kills: 5,
      deaths: 4,
      assists: 6,
      cs: 180,
      killParticipation: 0.5,
      items: [0, 0, 0, 0, 0, 0],
      trinketItemId: 3340,
      roleBoundItemId: 0,
      teamId: 100,
      position: 'MIDDLE',
      win: true,
      lpDelta: null,
      performanceScore: 50,
      placement: 5,
      isMvp: false,
      isAce: false,
      ...self,
    },
    participants: [],
  }
}

/** Newest first, as every history is served: the `recent` games' deaths, then the `older` ones'. */
function deathsRun(recent: number[], older: number[]): MatchSummaryResponse[] {
  return [...recent, ...older].map((deaths, index) => match(index, { deaths }))
}

const tile = (tiles: FormTile[], key: FormTile['key']) => tiles.find(entry => entry.key === key)!

describe('formTiles', () => {
  it('has no tile without a game', () => {
    expect(formTiles([])).toEqual([])
  })

  it('reads each tile as the average over the games, its series oldest first', () => {
    const tiles = formTiles([
      match(1, { kills: 10, deaths: 0, assists: 2, cs: 300, killParticipation: 0.6 }),
      match(2, { kills: 2, deaths: 2, assists: 2, cs: 150, killParticipation: 0.4 }, 1500),
    ])

    expect(tiles.map(entry => entry.key)).toEqual(['kda', 'killParticipation', 'csPerMinute', 'deaths'])
    // A deathless game counts as one death in its KDA: 12 / 1, never a division by zero.
    expect(tile(tiles, 'kda')).toMatchObject({ value: '7.00', series: [2, 12] })
    expect(tile(tiles, 'killParticipation').value).toBe('50%')
    // 150 CS over 25 minutes, then 300 over 30.
    expect(tile(tiles, 'csPerMinute')).toMatchObject({ value: '8.0', series: [6, 10] })
    expect(tile(tiles, 'deaths').value).toBe('1.0')
  })

  it('reads no move on a sample shorter than MIN_GAMES_FOR_DELTA', () => {
    const run = deathsRun([1, 1, 1, 1, 1], [9, 9])
    expect(run.length).toBeLessThan(MIN_GAMES_FOR_DELTA)
    expect(formTiles(run).map(entry => entry.delta)).toEqual([null, null, null, null])
  })

  it('reads the newest five games against the whole sample from MIN_GAMES_FOR_DELTA on', () => {
    // Eight games: 3.5 deaths on average, 2 over the newest five.
    const run = deathsRun([2, 2, 2, 2, 2], [6, 6, 6])
    expect(run).toHaveLength(MIN_GAMES_FOR_DELTA)
    expect(tile(formTiles(run), 'deaths')).toMatchObject({
      value: '3.5',
      delta: { text: '1.5', direction: 'down', good: true },
    })
  })

  it('reads fewer deaths and a higher KDA as good, the reverse as bad', () => {
    const better = formTiles(deathsRun([2, 2, 2, 2, 2], [6, 6, 6]))
    expect(tile(better, 'deaths').delta).toMatchObject({ direction: 'down', good: true })
    expect(tile(better, 'kda').delta).toMatchObject({ direction: 'up', good: true })

    const worse = formTiles(deathsRun([6, 6, 6, 6, 6], [2, 2, 2]))
    expect(tile(worse, 'deaths').delta).toMatchObject({ direction: 'up', good: false })
    expect(tile(worse, 'kda').delta).toMatchObject({ direction: 'down', good: false })
  })

  it('reads no move on a flat run', () => {
    expect(formTiles(deathsRun([4, 4, 4, 4, 4], [4, 4, 4])).map(entry => entry.delta)).toEqual([null, null, null, null])
  })
})
