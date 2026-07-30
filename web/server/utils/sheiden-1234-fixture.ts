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
 * Activity grid fixture (#927). Deliberately reproduces the shape prod actually
 * has rather than a full grid: the match-sourced series stop 24 days back (the
 * retention window), while the patch series carries the seven patches the
 * dedication card above claims — so the card's "patch history is longer than the
 * day history" copy can be eyeballed instead of imagined.
 */
export function buildSheidenActivityResponse(now: Date = new Date()): TruemainActivityResponse {
  // Deterministic pseudo-random so the grid looks lived-in but never flickers
  // between reloads. Same trick the full dev mock uses.
  let seed = 0x9e3779b9
  const rand = () => {
    seed = (seed * 1664525 + 1013904223) >>> 0
    return seed / 0x100000000
  }

  const retainedDays = 24

  interface Game { startUtc: Date, win: boolean, championId: number }
  const games: Game[] = []
  const pool = SHEIDEN_PROFILE.mains.map(main => main.championId)
  for (let day = retainedDays - 1; day >= 0; day--) {
    // A rest day every so often, so the day grid has genuine empty cells to
    // distinguish from the 0%-winrate ones.
    const perDay = rand() < 0.2 ? 0 : 1 + Math.floor(rand() * 4)
    for (let i = 0; i < perDay; i++) {
      games.push({
        startUtc: new Date(now.getTime() - day * DAY_MS + i * 45 * 60 * 1000),
        win: rand() < 0.58,
        championId: pool[Math.floor(rand() * pool.length)] ?? pool[0]!,
      })
    }
  }

  const isoDay = (date: Date) => date.toISOString().slice(0, 10)
  const floorDay = (date: Date) => new Date(`${isoDay(date)}T00:00:00.000Z`)
  const floorWeek = (date: Date) => {
    const day = floorDay(date)
    // ISO weeks start on Monday; getUTCDay() counts from Sunday.
    const offset = (day.getUTCDay() + 6) % 7
    return new Date(day.getTime() - offset * DAY_MS)
  }

  const calendar = (floor: (date: Date) => Date, stepDays: number) => {
    // The generator can in principle roll every day as a rest day; a fixture must
    // not crash on its own dice.
    if (games.length === 0) return []

    const totals = new Map<string, { games: number, wins: number }>()
    for (const game of games) {
      const key = isoDay(floor(game.startUtc))
      const current = totals.get(key) ?? { games: 0, wins: 0 }
      totals.set(key, { games: current.games + 1, wins: current.wins + (game.win ? 1 : 0) })
    }

    const first = floor(games[0]!.startUtc)
    const last = floor(now)
    const buckets = []
    for (let slot = first; slot <= last; slot = new Date(slot.getTime() + stepDays * DAY_MS)) {
      const key = isoDay(slot)
      const hit = totals.get(key)
      buckets.push({
        key,
        startUtc: slot.toISOString(),
        games: hit?.games ?? 0,
        wins: hit?.wins ?? 0,
        // Null, never 0, on an untouched slot.
        winRate: hit ? hit.wins / hit.games : null,
        championId: null,
      })
    }
    return buckets
  }

  const series = (
    mode: 'game' | 'day' | 'week',
    buckets: TruemainActivityResponse['day']['buckets'],
  ) => {
    const total = buckets.reduce((sum, bucket) => sum + bucket.games, 0)
    const wins = buckets.reduce((sum, bucket) => sum + bucket.wins, 0)
    return {
      mode,
      source: 'matches' as const,
      scope: 'allChampions' as const,
      championId: null,
      retentionBounded: true,
      coverageFromUtc: buckets[0]?.startUtc ?? null,
      coverageToUtc: buckets[buckets.length - 1]?.startUtc ?? null,
      buckets,
      games: total,
      wins,
      winRate: total === 0 ? null : wins / total,
    }
  }

  const gameBuckets = games.slice(-60).map(game => ({
    key: `EUW1_${game.startUtc.getTime()}`,
    startUtc: game.startUtc.toISOString(),
    games: 1,
    wins: game.win ? 1 : 0,
    winRate: game.win ? 1 : 0,
    championId: game.championId,
  }))

  // Seven patches summing to the 180 career games the dedication fixture claims,
  // so `patch.games === dedication.careerGames` holds here the way it does in
  // prod.
  const patchGames = [22, 24, 26, 28, 26, 30, 24]
  const patchBuckets = patchGames.map((count, index) => {
    const wins = Math.round(count * (0.5 + index * 0.02))
    return {
      key: `15.${index + 7}`,
      startUtc: null,
      games: count,
      wins,
      winRate: wins / count,
      championId: null,
    }
  })
  const patchTotal = patchBuckets.reduce((sum, bucket) => sum + bucket.games, 0)
  const patchWins = patchBuckets.reduce((sum, bucket) => sum + bucket.wins, 0)

  return {
    game: series('game', gameBuckets),
    day: series('day', calendar(floorDay, 1)),
    week: series('week', calendar(floorWeek, 7)),
    patch: {
      mode: 'patch',
      source: 'aggregates',
      scope: 'champion',
      championId: SHEIDEN_PROFILE.dedication!.championId,
      retentionBounded: false,
      coverageFromUtc: null,
      coverageToUtc: null,
      buckets: patchBuckets,
      games: patchTotal,
      wins: patchWins,
      winRate: patchWins / patchTotal,
    },
  }
}
