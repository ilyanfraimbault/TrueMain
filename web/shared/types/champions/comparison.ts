// The account-vs-mains comparison on a champion page.
import type { ProfileIdentity } from '../profile'

/**
 * Why an account-vs-mains comparison did — or did not — produce two comparable
 * columns. Mirrors `ChampionComparisonStatus` in
 * backend/Api/ReadModels/Champions/ChampionMainsComparisonResponse.cs.
 */
export type ChampionComparisonStatus
  = | 'OK'
    | 'UNKNOWN_ACCOUNT'
    | 'UNKNOWN_TARGET'
    | 'INSUFFICIENT_SAMPLE'

/** One column of the comparison. Counting stats are per-game averages. */
export interface ChampionComparisonSide {
  /** Null for the aggregate of the champion's mains — it has no single owner. */
  identity: ProfileIdentity | null
  /** Distinct accounts behind the column: 1 for a player, the pool size for the aggregate. */
  players: number
  games: number
  wins: number
  /** `wins / games`; 0 when the side has no games. */
  winRate: number
  /** Kills per game. */
  kills: number
  /** Deaths per game. */
  deaths: number
  /** Assists per game. */
  assists: number
  /** `(kills + assists) / deaths`, falling back to `kills + assists` on a deathless sample. */
  kda: number
  /** Minions + monsters per minute, over the summed game durations. */
  csPerMin: number
  /** Gold per minute, same denominator as `csPerMin`. */
  goldPerMin: number
  goldPerGame: number
  /** Whether `games` reached the response's `minGames` floor. */
  sampleMet: boolean
}

/**
 * Head-to-head between a Riot account and a champion's mains (#528). The
 * lookup is database-only — an account we have never ingested comes back as
 * `UNKNOWN_ACCOUNT` with no columns, never as an error.
 */
export interface ChampionMainsComparison {
  championId: number
  /** Resolved patch, or null when the slice spans every stored patch. */
  patch: string | null
  /** Lane both sides were narrowed to, or null for every lane. */
  position: string | null
  /** Games each side needs before the comparison counts as meaningful. */
  minGames: number
  status: ChampionComparisonStatus
  /** Null only when the account is unknown to us. */
  player: ChampionComparisonSide | null
  /** Null when the account — or a targeted main — is unknown to us. */
  mains: ChampionComparisonSide | null
}
