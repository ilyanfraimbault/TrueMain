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

export interface PersonaAccount {
  /** The account's current Riot ID when tracked (it follows renames), else the one the source published. */
  gameName: string
  tagLine: string
  platformId: string
  /** Null for a platform the site does not expose. */
  region: RegionSlug | null
  /** True when TrueMain tracks the account, i.e. it has a profile to link to. */
  tracked: boolean
  profileIconId: number | null
  ranked: PersonaAccountRanked | null
  topChampions: PersonaAccountChampion[]
}

export interface PersonaResponse {
  slug: string
  displayName: string
  links: PersonaLink[]
  /** Tracked accounts first, best rank first; then the untracked ones. */
  accounts: PersonaAccount[]
}
