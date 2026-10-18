// Mirrors backend/Api/ReadModels/Personas/PersonaReadModel.cs (#2016).
//
// A known player — a pro, a streamer, a creator, or any notable player — and
// the Riot accounts public sources list for them. "Persona" is the API's word;
// the site only ever shows the person's name, at `/players/{slug}`.

import type { RegionSlug } from './leaderboard'

/** The person a tracked account belongs to, as the leaderboard row and the profile carry it. */
export interface PersonaTag {
  slug: string
  displayName: string
}

export type PersonaLinkKind =
  | 'twitch' | 'youtube' | 'x' | 'kick' | 'tiktok' | 'instagram' | 'soop' | 'chzzk'
  | 'leaguepedia' | 'lolpros' | 'deeplol' | 'website'

export interface PersonaLink {
  kind: PersonaLinkKind
  url: string
}

export interface PersonaAccountRanked {
  tier: string
  division: string
  leaguePoints: number
  wins: number | null
  losses: number | null
}

export interface PersonaAccountChampion {
  championId: number
  games: number
  /** Share of the account's games on this champion, 0..1. */
  playRate: number
  isOtp: boolean
}

/** One tracked account of a known player — every one has a profile to open. */
export interface PersonaAccount {
  /** The account's current Riot ID (it follows renames). */
  gameName: string
  tagLine: string
  platformId: string
  /** Null for a platform the site does not expose. */
  region: RegionSlug | null
  profileIconId: number
  /** Start of the most recent ranked game TrueMain stored for it; null when it has none. */
  lastPlayedAtUtc: string | null
  ranked: PersonaAccountRanked | null
  topChampions: PersonaAccountChampion[]
}

export interface PersonaResponse {
  slug: string
  displayName: string
  links: PersonaLink[]
  /** Tracked accounts only, the one played most recently first, then by rank. */
  accounts: PersonaAccount[]
}

/** `GET /players` (#2018): the known players worth a sitemap entry — those with a tracked account. */
export interface PersonaDirectoryResponse {
  players: { slug: string, updatedAtUtc: string }[]
}
