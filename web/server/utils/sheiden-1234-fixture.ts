// Dev-only fixture for `/truemains/Sheiden-1234`. Used by the three route
// handlers under `server/api/truemains/Sheiden-1234/` to short-circuit the
// API proxy and serve a deterministic Master → Grandmaster → Challenger
// climb so the unified ranked card can be eyeballed without a live backend.
// The handlers return 404 outside dev (`import.meta.dev`), so this fixture
// data never reaches a deployed (production / QA) environment.

import type { TruemainActivityResponse } from '~~/shared/types/activity'
import type { MatchSummariesResponse } from '~~/shared/types/matches'
import type { ProfileResponse } from '~~/shared/types/profile'
import type { RankHistoryEntry, RankHistoryResponse } from '~~/shared/types/rank-history'

const DAY_MS = 24 * 60 * 60 * 1000

// This fixture describes a freshly-measured account, so its mains are never
// flagged as retired (#1216) and their measurement date is "now".
const FIXTURE_MEASURED_AT = new Date().toISOString()

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
    { championId: 64, games: 180, playRate: 0.30, primaryPosition: 'JUNGLE', isOtp: false, isSampleRetired: false, measuredAtUtc: FIXTURE_MEASURED_AT },
    { championId: 91, games: 140, playRate: 0.23, primaryPosition: 'JUNGLE', isOtp: false, isSampleRetired: false, measuredAtUtc: FIXTURE_MEASURED_AT },
    { championId: 121, games: 96, playRate: 0.16, primaryPosition: 'JUNGLE', isOtp: false, isSampleRetired: false, measuredAtUtc: FIXTURE_MEASURED_AT },
    { championId: 11, games: 70, playRate: 0.12, primaryPosition: 'JUNGLE', isOtp: false, isSampleRetired: false, measuredAtUtc: FIXTURE_MEASURED_AT },
  ],
  // Dedication on the top main (Kha'Zix): a wide-pool jungler, so commitment is
  // modest while span / volume / recency are strong. Values mirror what
  // backend/Core/Truemains/DedicationScore.cs would produce for these inputs.
  dedication: {
    score: 62.9,
    championId: 64,
    commitment: 0.205,
    span: 1,
    volume: 0.98,
    recency: 0.936,
    playRate: 0.30,
    careerGames: 180,
    patchSpan: 7,
    daysSinceLastGame: 2,
  },
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

/**
 * Activity grid fixture (#927, reshaped in #1473). One unit — the day — over
 * three windows, the same shape the endpoint serves: the patch window opens
 * twelve days back (the patch's first day, measured server-side over everyone's
 * matches), the week window is the last seven days, and the day window is
 * whatever was played today.
 */
export function buildSheidenActivityResponse(now: Date = new Date()): TruemainActivityResponse {
  // Deterministic pseudo-random so the grid looks lived-in but never flickers
  // between reloads. Same trick the full dev mock uses.
  let seed = 0x9e3779b9
  const rand = () => {
    seed = (seed * 1664525 + 1013904223) >>> 0
    return seed / 0x100000000
  }

  // The patch opened twelve days ago; the player only turned up on day three of
  // it, so the grid opens on a run of idle tiles — the case the window exists to
  // draw, and the one a per-player bound would silently hide.
  const patchDays = 12
  const firstPlayedDay = 9

  interface Game { matchId: string, startUtc: Date, win: boolean, championId: number }
  const games: Game[] = []
  const pool = SHEIDEN_PROFILE.mains.map(main => main.championId)
  for (let day = firstPlayedDay; day >= 0; day--) {
    // A rest day every so often, so the grid has genuine empty cells to
    // distinguish from the 0%-winrate ones. Never today, or the day window would
    // open on its empty state.
    const perDay = day > 0 && rand() < 0.2 ? 0 : 1 + Math.floor(rand() * 4)
    for (let i = 0; i < perDay; i++) {
      const startUtc = new Date(now.getTime() - day * DAY_MS + i * 45 * 60 * 1000)
      games.push({
        matchId: `EUW1_${startUtc.getTime()}`,
        startUtc,
        win: rand() < 0.58,
        championId: pool[Math.floor(rand() * pool.length)] ?? pool[0]!,
      })
    }
  }

  const isoDay = (date: Date) => date.toISOString().slice(0, 10)
  const floorDay = (date: Date) => new Date(`${isoDay(date)}T00:00:00.000Z`)

  const byDay = (firstDay: Date, lastDay: Date) => {
    const totals = new Map<string, { games: number, wins: number }>()
    for (const game of games) {
      const key = isoDay(game.startUtc)
      const current = totals.get(key) ?? { games: 0, wins: 0 }
      totals.set(key, { games: current.games + 1, wins: current.wins + (game.win ? 1 : 0) })
    }

    const buckets = []
    for (let slot = firstDay; slot <= lastDay; slot = new Date(slot.getTime() + DAY_MS)) {
      const key = isoDay(slot)
      const hit = totals.get(key)
      buckets.push({
        key,
        startUtc: slot.toISOString(),
        games: hit?.games ?? 0,
        wins: hit?.wins ?? 0,
        // Null, never 0, on an untouched day.
        winRate: hit ? hit.wins / hit.games : null,
        championId: null,
      })
    }
    return buckets
  }

  const series = (
    mode: 'patch' | 'week' | 'day',
    patch: string | null,
    buckets: TruemainActivityResponse['day']['buckets'],
  ) => {
    const total = buckets.reduce((sum, bucket) => sum + bucket.games, 0)
    const wins = buckets.reduce((sum, bucket) => sum + bucket.wins, 0)
    return {
      mode,
      patch,
      coverageFromUtc: buckets[0]?.startUtc ?? null,
      coverageToUtc: buckets[buckets.length - 1]?.startUtc ?? null,
      buckets,
      games: total,
      wins,
      winRate: total === 0 ? null : wins / total,
    }
  }

  const today = floorDay(now)
  const dayBuckets = games
    .filter(game => isoDay(game.startUtc) === isoDay(now))
    .map(game => ({
      key: game.matchId,
      startUtc: game.startUtc.toISOString(),
      games: 1,
      wins: game.win ? 1 : 0,
      winRate: game.win ? 1 : 0,
      championId: game.championId,
    }))

  return {
    patch: series('patch', '15.14', byDay(new Date(today.getTime() - patchDays * DAY_MS), today)),
    week: series('week', null, byDay(new Date(today.getTime() - 6 * DAY_MS), today)),
    day: series('day', null, dayBuckets),
  }
}
