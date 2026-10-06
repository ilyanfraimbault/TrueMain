import type { GamePlayer } from '~/types/game'
import type { LoadingPlayer, PlayerForm, PositionGames } from '~/types/loading'
import type { RankedQueue } from '~/types/record'

/**
 * What the Game page says about each player (#1828), read off what the
 * loading screen already fetched through the client (`src-tauri/src/loading.rs`):
 * their standing, whether they are on their role, how they do on the champion
 * they are on, and the streak they are on. Counts and ratios only — no score
 * made of them (#1671).
 */

/**
 * The loading line of a player the game lists: by Riot ID, else — an
 * anonymous player, whose name the loading screen never kept — by side and
 * champion, which no two players of a side share.
 */
export function loadingLineOf(
  player: GamePlayer,
  lines: LoadingPlayer[],
  championIdOf: (alias: string) => number | undefined,
): LoadingPlayer | null {
  const riotId = player.riotId.toLowerCase()
  const byName = riotId ? lines.find(line => line.riotId && line.riotId.toLowerCase() === riotId) : undefined
  if (byName) return byName
  const championId = championIdOf(player.champion)
  if (championId === undefined) return null
  return lines.find(line => line.team === player.team && line.championId === championId) ?? null
}

// ─── Role ───────────────────────────────────────────────────────────────────

export type RoleFitKind = 'main' | 'secondary' | 'autofill'

export interface RoleFit {
  kind: RoleFitKind
  /** The role played in this game. */
  position: string
  /** The role played most, when it is not this one. */
  usual: string | null
  /** Games in this role, over the games of a queue that assigns roles. */
  games: number
  roleGames: number
}

/** Fewer role-assigned games than this say nothing about a player's role. */
const MIN_ROLE_GAMES = 3
/** This share of the role-assigned games in a role makes it the player's own. */
const MAIN_SHARE = 0.5
/** Below this share the role was given to them: an autofill. */
const AUTOFILL_SHARE = 0.25

/** Whether `position` is the player's role, from the roles their recent games gave them; null when unknown. */
export function roleFit(position: string, positions: PositionGames[]): RoleFit | null {
  const roleGames = positions.reduce((sum, entry) => sum + entry.games, 0)
  if (!position || roleGames < MIN_ROLE_GAMES) return null
  const games = positions.find(entry => entry.position === position)?.games ?? 0
  const share = games / roleGames
  const top = positions[0]
  const usual = top && top.position !== position ? top.position : null
  const kind: RoleFitKind = share >= MAIN_SHARE ? 'main' : share >= AUTOFILL_SHARE ? 'secondary' : 'autofill'
  return { kind, position, usual, games, roleGames }
}

// ─── Champion ───────────────────────────────────────────────────────────────

export interface ChampionRecord {
  games: number
  /** 0..100, whole. */
  winRate: number
  /** (kills + assists) / deaths over those games, deaths floored at one. */
  kda: number
  kills: number
  deaths: number
  assists: number
}

/** The player's games on the champion they are on, among those read; null on a first time. */
export function championRecord(form: PlayerForm): ChampionRecord | null {
  if (form.championGames === 0) return null
  const games = form.championGames
  return {
    games,
    winRate: Math.round((form.championWins / games) * 100),
    kda: (form.championKills + form.championAssists) / Math.max(form.championDeaths, 1),
    kills: form.championKills / games,
    deaths: form.championDeaths / games,
    assists: form.championAssists / games,
  }
}

/** A KDA this high on a champion is drawn as good. */
export const GOOD_KDA = 3

// ─── Streak ─────────────────────────────────────────────────────────────────

/** A run shorter than this is not a streak worth a chip. */
export const MIN_STREAK = 3

export interface Streak {
  wins: boolean
  games: number
}

export function streakOf(form: PlayerForm): Streak | null {
  const games = Math.abs(form.streak)
  return games >= MIN_STREAK ? { wins: form.streak > 0, games } : null
}

/** Wins over the latest games read, as a share; null with none. */
export function recentWinRate(form: PlayerForm): number | null {
  if (!form.recent.length) return null
  return Math.round((form.recent.filter(game => game.win).length / form.recent.length) * 100)
}

// ─── Rank ───────────────────────────────────────────────────────────────────

export const isRanked = (rank: RankedQueue) => Boolean(rank.tier) && rank.tier !== 'NONE'

/** `Gold`, `Grandmaster`: the tier the way the line prints it. */
export const tierName = (tier: string) => tier.charAt(0) + tier.slice(1).toLowerCase()

/** The season's record in that queue, as a share; null with no game. */
export function seasonWinRate(rank: RankedQueue): number | null {
  const games = rank.wins + rank.losses
  return games ? Math.round((rank.wins / games) * 100) : null
}

// ─── True main ──────────────────────────────────────────────────────────────

/** One player TrueMain tracks as a true main of the champion they are on (#1910): `GET /truemains/lookup`. */
export interface TruemainMark {
  /** `Name#TAG` as TrueMain stores it: matched without regard to case. */
  riotId: string
  nameTag: string
  championId: number
  /** Recent ranked games on the champion, over `totalMatches`. */
  championMatches: number
  totalMatches: number
  playRate: number
  isOtp: boolean
  masteryPoints: number | null
  /** The Truemain score on the champion, 0..100. */
  dedication: number
}

/**
 * The players the lookup is asked about, as `Name#TAG:championId`, sorted: the
 * named ones on a champion. An anonymous player (Streamer Mode) is never sent —
 * nothing is asked about a player whose name the client keeps from us.
 */
export function lookupPlayers(lines: LoadingPlayer[]): string[] {
  return lines
    .filter(line => !line.anonymous && line.riotId.includes('#') && line.championId > 0)
    .map(line => `${line.riotId}:${line.championId}`)
    .sort()
}

/** The mark of a loading line: their Riot ID and the champion they are on, both matching. */
export function markOf(marks: TruemainMark[], line: LoadingPlayer | null): TruemainMark | null {
  if (!line || line.anonymous || !line.riotId) return null
  const riotId = line.riotId.toLowerCase()
  return marks.find(mark => mark.championId === line.championId && mark.riotId.toLowerCase() === riotId) ?? null
}

/** What the mark's tooltip says under its title: only figures the lookup returned. */
export function markFigures(mark: TruemainMark): string[] {
  const figures: string[] = []
  if (mark.totalMatches > 0) figures.push(`${mark.championMatches} of their last ${mark.totalMatches} ranked games`)
  if (mark.isOtp) figures.push('One-trick')
  figures.push(`Truemain score ${Math.round(mark.dedication)}`)
  return figures
}
