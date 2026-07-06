// Dev-only fixture for `/truemains/Sheiden-1234`. Used by the three route
// handlers under `server/api/truemains/Sheiden-1234/` to short-circuit the
// API proxy and serve a deterministic Master → Grandmaster → Challenger
// climb so the unified ranked card can be eyeballed without a live backend.
// The handlers return 404 outside dev (`import.meta.dev`), so this fixture
// data never reaches a deployed (production / QA) environment.

import type { MatchSummariesResponse } from '~~/shared/types/matches'
import type { ProfileResponse } from '~~/shared/types/profile'
import type { RankHistoryEntry, RankHistoryResponse } from '~~/shared/types/rank-history'

const DAY_MS = 24 * 60 * 60 * 1000

export const SHEIDEN_PROFILE: ProfileResponse = {
  identity: {
    gameName: 'Sheiden',
    tagLine: '1234',
    platformId: 'EUW1',
    profileIconId: 4567,
    summonerLevel: 614,
  },
  ranked: {
    tier: 'CHALLENGER',
    division: 'I',
    leaguePoints: 1247,
    wins: 312,
    losses: 248,
    winRate: 312 / (312 + 248),
  },
  mains: [
    // Mid-lane heavy apex profile — keeps the right rail looking lived-in
    // even though the matches endpoint returns empty below.
    { championId: 64, games: 180, playRate: 0.30, primaryPosition: 'JUNGLE', isOtp: false },
    { championId: 91, games: 140, playRate: 0.23, primaryPosition: 'JUNGLE', isOtp: false },
    { championId: 121, games: 96, playRate: 0.16, primaryPosition: 'JUNGLE', isOtp: false },
    { championId: 11, games: 70, playRate: 0.12, primaryPosition: 'JUNGLE', isOtp: false },
  ],
  positions: [
    { position: 'JUNGLE', games: 520, rate: 520 / 600 },
    { position: 'MIDDLE', games: 60, rate: 60 / 600 },
    { position: 'TOP', games: 20, rate: 20 / 600 },
  ],
}

// Tier cutoffs we apply to LP for label purposes — Riot's actual GM/Chall
// thresholds drift with the regional ladder but ~500 LP / ~900 LP is a
// reasonable EUW-ish proxy and matches what the rank-score helper assumes
// for the continuous apex band. Exported so the full-API dev mock
// (dev-api-mock.ts) labels its generated rank histories with the same cutoffs.
export function apexTierForLp(lp: number): { tier: string, division: string } {
  if (lp >= 900) return { tier: 'CHALLENGER', division: 'I' }
  if (lp >= 500) return { tier: 'GRANDMASTER', division: 'I' }
  return { tier: 'MASTER', division: 'I' }
}

/**
 * 60 daily-ish snapshots tracing a smooth Master → GM → Challenger climb
 * with a few small dips so the chart isn't a straight diagonal. Anchored at
 * `now` so the last entry matches the headline ranked card.
 */
export function buildSheidenRankHistory(now: Date = new Date()): RankHistoryEntry[] {
  const entries: RankHistoryEntry[] = []
  // Smooth ramp from 80 LP (Master) to 1247 LP (Challenger) over 90 days.
  const startLp = 80
  const endLp = 1247
  const days = 90

  for (let day = days - 1; day >= 0; day--) {
    const progress = 1 - day / (days - 1)
    // Mild easing so the late-stage climb has a faster slope, mimicking how
    // an actual climber rolls win streaks once they break GM.
    const eased = progress * progress * (3 - 2 * progress)
    const base = startLp + (endLp - startLp) * eased
    // Tiny sinusoidal wobble so the line breathes rather than being a pure
    // monotonic curve.
    const wobble = Math.sin(day / 3.7) * 12 + Math.sin(day / 11) * 6
    const lp = Math.max(0, Math.round(base + wobble))
    const { tier, division } = apexTierForLp(lp)
    entries.push({
      capturedAtUtc: new Date(now.getTime() - day * DAY_MS).toISOString(),
      tier,
      division,
      leaguePoints: lp,
    })
  }

  // Pin the final entry to the headline rank so the chart endpoint matches
  // the big LP number above it.
  entries[entries.length - 1] = {
    capturedAtUtc: now.toISOString(),
    tier: SHEIDEN_PROFILE.ranked!.tier,
    division: SHEIDEN_PROFILE.ranked!.division,
    leaguePoints: SHEIDEN_PROFILE.ranked!.leaguePoints,
  }

  return entries
}

export function buildSheidenRankHistoryResponse(): RankHistoryResponse {
  return { entries: buildSheidenRankHistory() }
}

export const SHEIDEN_EMPTY_MATCHES: MatchSummariesResponse = {
  matches: [],
  page: 1,
  pageSize: 20,
  total: 0,
}
