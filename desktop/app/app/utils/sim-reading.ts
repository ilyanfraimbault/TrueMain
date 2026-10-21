import type { GamePlayer, GameState, GameTeam } from '~/types/game'
import { sampleGame } from '~/utils/overlay-sample'

/**
 * The game simulator's (`pages/dev/game-sim.vue`) look at the reading it just
 * sent: enough of the game state for the lane-gold plates (#2186) — each
 * player's side, lane and items — read off the raw `allgamedata` the way the
 * shell's feed reads them (`crates/live-client`). Development only; anything
 * else comes from the overlay's sample game.
 */

interface RawItem { itemID?: number, slot?: number, count?: number }
interface RawPlayer {
  championName?: string
  rawChampionName?: string
  riotId?: string
  team?: string
  position?: string
  items?: RawItem[]
}
interface RawReading { activePlayer?: { riotId?: string }, allPlayers?: RawPlayer[], gameData?: { gameTime?: number } }

/** The reading's players on the sample game; null when it holds none. */
export function gameOfReading(data: unknown): GameState | null {
  const reading = (data ?? {}) as RawReading
  const raw = reading.allPlayers ?? []
  if (!raw.length) return null
  const base = sampleGame()
  const template = base.players[0]!
  const me = reading.activePlayer?.riotId ?? null
  const players: GamePlayer[] = raw.map(player => ({
    ...template,
    riotId: player.riotId ?? '',
    champion: player.rawChampionName?.split('_').pop() ?? player.championName ?? '',
    championName: player.championName ?? '',
    team: (player.team === 'CHAOS' ? 'CHAOS' : 'ORDER') as GameTeam,
    position: player.position ?? '',
    isMe: me !== null && player.riotId === me,
    items: (player.items ?? []).map(item => ({ itemId: item.itemID ?? 0, slot: item.slot ?? 0, count: item.count ?? 1 })),
  }))
  return {
    ...base,
    gameTime: reading.gameData?.gameTime ?? 0,
    myTeam: players.find(player => player.isMe)?.team ?? null,
    players,
  }
}
