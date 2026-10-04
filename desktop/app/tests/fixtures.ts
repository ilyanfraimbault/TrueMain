import type { RankHistoryEntry } from '#shared/types/rank-history'
import type { PlayerGame } from '~/types/record'

export const MINUTE = 60_000
export const DAY = 86_400_000

/** A Solo/Duo game of thirty minutes, with every field a test does not name at a neutral value. */
export function game(overrides: Partial<PlayerGame> = {}): PlayerGame {
  return {
    gameId: 1,
    playedAt: 0,
    durationSeconds: 1800,
    queueId: 420,
    mapId: 11,
    championId: 1,
    championLevel: 18,
    teamId: 100,
    position: 'MIDDLE',
    win: true,
    remake: false,
    kills: 5,
    deaths: 5,
    assists: 5,
    cs: 200,
    gold: 12_000,
    damageToChampions: 20_000,
    visionScore: 20,
    largestMultiKill: 1,
    items: [0, 0, 0, 0, 0, 0],
    trinket: 0,
    roleBoundItem: 0,
    spells: [4, 14],
    keystone: 8112,
    primaryStyle: 8100,
    subStyle: 8300,
    teamKills: 20,
    teamDamageToChampions: 80_000,
    participants: [],
    ...overrides,
  }
}

export function snapshot(at: number, leaguePoints: number, tier = 'GOLD', division = 'II'): RankHistoryEntry {
  return { capturedAtUtc: new Date(at).toISOString(), tier, division, leaguePoints }
}
