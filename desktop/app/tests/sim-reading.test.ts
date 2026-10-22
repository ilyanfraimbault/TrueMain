import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'
import { scoreboardPairs } from '~/utils/lane-gold'
import { gameOfReading } from '~/utils/sim-reading'

// The game simulator's tape (`pages/dev/game-sim.vue`), outside the app's root.
const tape = readFileSync(join(import.meta.dirname, '../../fixtures/ranked-game.jsonl'), 'utf8')
  .split('\n')
  .filter(line => line.trim())
  .map(line => JSON.parse(line) as { kind: string, data: unknown })
const readings = tape.filter(line => line.kind === 'game').map(line => line.data)

describe('the game simulator\'s reading', () => {
  it('reads each player\'s side, lane and items off the tape', () => {
    const game = gameOfReading(readings.at(-1))!
    expect(game.players).toHaveLength(10)
    const top = game.players[0]!
    expect(top.team).toBe('ORDER')
    expect(top.position).toBe('TOP')
    expect(top.items[0]).toEqual({ itemId: 6692, slot: 0, count: 1 })
    expect(scoreboardPairs(game).map(({ left, right }) => [left?.position, right?.position])).toEqual([
      ['TOP', 'TOP'],
      ['JUNGLE', 'JUNGLE'],
      ['MIDDLE', 'MIDDLE'],
      ['BOTTOM', 'BOTTOM'],
      ['UTILITY', 'UTILITY'],
    ])
    expect(game.gameTime).toBeGreaterThan(0)
  })

  it('reads nothing out of a reading without players', () => {
    expect(gameOfReading(undefined)).toBeNull()
    expect(gameOfReading({ allPlayers: [] })).toBeNull()
  })

  it('finds us by Riot ID, and reads a player\'s missing fields as empty', () => {
    const game = gameOfReading({
      activePlayer: { riotId: 'Me#1' },
      allPlayers: [{ riotId: 'Me#1', team: 'CHAOS', rawChampionName: 'game_character_displayname_Ahri' }, {}],
    })!
    expect(game.myTeam).toBe('CHAOS')
    expect(game.players[0]!.champion).toBe('Ahri')
    expect(game.players[1]).toMatchObject({ riotId: '', team: 'ORDER', position: '', isMe: false, items: [] })
  })
})
