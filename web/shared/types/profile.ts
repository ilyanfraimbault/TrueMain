// Mirrors backend/Api/ReadModels/Truemains/ProfileReadModel.cs.
// See #118.

import type { TruemainDedication } from './dedication'

export interface ProfileResponse {
  identity: ProfileIdentity
  /** Null when the player has no ranked snapshot yet (unranked or not refreshed). */
  ranked: ProfileRanked | null
  mains: ProfileMainChampion[]
  /** Dedication score for the player's signature champion. Null when no champion is classified as a main yet. */
  dedication: TruemainDedication | null
  positions: ProfilePositionStat[]
}

export interface ProfileIdentity {
  gameName: string
  /** Null when the row was ingested before tag lines were stored. */
  tagLine: string | null
  platformId: string
  profileIconId: number
  summonerLevel: number
}

export interface ProfileRanked {
  tier: string
  division: string
  leaguePoints: number
  /** Null when Riot's league response omitted it. */
  wins: number | null
  /** Null when Riot's league response omitted it. */
  losses: number | null
  /** `wins / (wins + losses)` when both are present, otherwise null. */
  winRate: number | null
}

export interface ProfileMainChampion {
  championId: number
  games: number
  /** `games / total games on the account` from the main analysis (0..1). */
  playRate: number
  /** Riot team position string (uppercase, e.g. `MIDDLE`). Empty when no dominant lane. */
  primaryPosition: string
  isOtp: boolean
  /**
   * True when the matches these figures were computed from have aged out of
   * retention (#1216), so `games` and `playRate` describe a sample the site no
   * longer holds. The row is kept on purpose — the player stays on the
   * leaderboard — but it must be shown dated by `measuredAtUtc` rather than as
   * a current count, or the profile promises games the champion page can't show.
   */
  isSampleRetired: boolean
  /** When the main analysis last recomputed these figures (ISO 8601). */
  measuredAtUtc: string
}

export interface ProfilePositionStat {
  position: string
  games: number
  /** `games / sum(games) across the player's mains` (0..1). */
  rate: number
}
