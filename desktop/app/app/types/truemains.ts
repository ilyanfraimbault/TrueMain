/**
 * The slice of `GET /truemains` (the site's leaderboard) the app draws.
 * Mirrors `web/shared/types/leaderboard.ts`; fields left out are not read here.
 */
export interface TruemainIdentity {
  gameName: string
  tagLine: string | null
  platformId: string
  profileIconId: number
  summonerLevel: number
}

export interface TruemainRanked {
  tier: string | null
  division: string | null
  leaguePoints: number | null
}

export interface TruemainTopChampion {
  championId: number
  games: number
  playRate: number
  isOtp: boolean
  primaryKeystoneId: number | null
  secondaryStyleId: number | null
  firstItemId: number | null
}

export interface TruemainRow {
  rank: number
  identity: TruemainIdentity
  region: string
  ranked: TruemainRanked
  stats: { games: number, wins: number, losses: number, winRate: number, kda: number }
  topChampions: TruemainTopChampion[]
  positions: { primary: string | null, secondary: string | null }
}

export interface LeaderboardResponse {
  rows: TruemainRow[]
  page: number
  pageSize: number
  total: number
}

/** The site's profile slug: `{gameName}-{tagLine}`, or the name alone when untagged. */
export function nameTagOf(identity: TruemainIdentity): string {
  return identity.tagLine ? `${identity.gameName}-${identity.tagLine}` : identity.gameName
}

/** The player's page on the site. */
export function profilePath(identity: TruemainIdentity): string {
  return `/truemains/${encodeURIComponent(nameTagOf(identity))}`
}

/** Community Dragon's ranked crest — the same source as the site's `RankIcon`. */
export function rankCrestUrl(tier: string | null | undefined): string | null {
  const value = tier?.trim().toLowerCase()
  if (!value) return null
  return `https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-static-assets/global/default/images/ranked-mini-crests/${value}.svg`
}

const APEX = new Set(['MASTER', 'GRANDMASTER', 'CHALLENGER'])

/** `Challenger 1,284 LP` / `Diamond II` — the division is noise at the apex. */
export function formatRank(ranked: TruemainRanked): string {
  if (!ranked.tier) return 'Unranked'
  const tier = ranked.tier.charAt(0) + ranked.tier.slice(1).toLowerCase()
  if (APEX.has(ranked.tier)) return `${tier} ${(ranked.leaguePoints ?? 0).toLocaleString('en-US')} LP`
  return `${tier} ${ranked.division ?? ''}`.trim()
}
