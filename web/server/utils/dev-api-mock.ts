// Dev-only mock of the whole backend read API, opted into with
// `NUXT_DEV_MOCK_API=1`. The catch-all proxy (`server/api/[...path].ts`)
// consults `resolveDevApiMock` before proxying, inside an `import.meta.dev`
// guard, so none of this ships in a production build and a dev run *without*
// the env flag still proxies to a real local backend.
//
// Same philosophy as `sheiden-1234-fixture.ts` (which keeps precedence for
// `/truemains/Sheiden-1234/*` via its explicit route files): deterministic,
// typed against the shared read models, and realistic enough to eyeball every
// page — champion list + detail (builds, runes, matchups, charts), truemains
// leaderboard, player profiles and match history — without a live backend.
// All ids (champions, items, runes, summoner spells) are real Riot ids so the
// DDragon/CDragon-backed `/api/static/*` endpoints resolve genuine icons.

import type {
  ActivityBucket,
  ActivitySeries,
  TruemainActivityResponse,
} from '~~/shared/types/activity'
import type {
  ChampionBuild,
  ChampionComparisonSide,
  ChampionMainsComparison,
  ChampionMatchupEntry,
  ChampionMatchups,
  ChampionOverviewResponse,
  ChampionPatchDiffResponse,
  ChampionPatchDiffSide,
  ChampionPowerspikeEvent,
  ChampionPowerspikesResponse,
  ChampionResponse,
  ChampionRoamResponse,
  ChampionScalingResponse,
  ChampionSummaryResponse,
  ChampionSynergies,
  ChampionSynergyEntry,
  ChampionTrendResponse,
  ChampionTrioSynergies,
  ChampionTrioSynergyEntry,
  BuildRunePage,
} from '~~/shared/types/champions'
import type {
  LeaderboardResponse,
  LeaderboardRowResponse,
  RegionSlug,
} from '~~/shared/types/leaderboard'
import type { CompositionBuildGamesResponse, CompositionBuildResponse, CompositionGame } from '~~/shared/types/composition'
import type { TruemainDedication } from '~~/shared/types/dedication'
import type {
  BuildChoice,
  BuildDivergence,
  PlayerBuildDivergenceResponse,
} from '~~/shared/types/divergence'
import type { MatchSummariesResponse, MatchSummaryResponse } from '~~/shared/types/matches'
import type {
  PerformanceComponentKind,
  PlayerChampionPerformanceResponse,
} from '~~/shared/types/performance'
import { PERFORMANCE_COMPONENT_KINDS } from '~~/shared/types/performance'
import type { ProfileIdentity, ProfileResponse } from '~~/shared/types/profile'
import type { RankHistoryResponse } from '~~/shared/types/rank-history'
import type { SearchResponse } from '~~/shared/types/search'

// ─── Deterministic PRNG ──────────────────────────────────────────────────────
// mulberry32 — every payload derives from stable seeds so repeated fetches
// (and the SSR/client pair) always see identical data.

function mulberry32(seed: number): () => number {
  let a = seed >>> 0
  return () => {
    a += 0x6D2B79F5
    let t = a
    t = Math.imul(t ^ (t >>> 15), t | 1)
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61)
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

const round3 = (value: number) => Math.round(value * 1000) / 1000

// ─── Live patch (memoized) ───────────────────────────────────────────────────
// The champions page pins its static-data fetches (items, rune tree) to the
// list's `patchVersion`, so the mock must advertise the real current patch or
// icons would resolve against a stale/unknown CDragon tree.

let patchPromise: Promise<string> | null = null

function latestShortPatch(): Promise<string> {
  patchPromise ??= $fetch<string[]>('https://ddragon.leagueoflegends.com/api/versions.json')
    .then((versions) => {
      const [latest] = versions
      if (!latest) return '15.1'
      const [major, minor] = latest.split('.')
      return `${major}.${minor}`
    })
    .catch(() => '15.1')
  return patchPromise
}

/** Previous short patches for the trend chart, newest last. */
function trendPatches(latest: string, count: number): string[] {
  const [major = 15, minor = 1] = latest.split('.').map(Number)
  const patches: string[] = []
  for (let i = count - 1; i >= 0; i--) {
    // Walk minor versions backwards, wrapping into the previous season at .1.
    const m = minor - i
    patches.push(m >= 1 ? `${major}.${m}` : `${major - 1}.${24 + m}`)
  }
  return patches
}

// ─── Archetypes: shared item/spell/skill templates ───────────────────────────

const SPELLS = { flash: 4, ignite: 14, teleport: 12, heal: 7, exhaust: 3, barrier: 21, smite: 11, ghost: 6 } as const

interface Archetype {
  starterItems: number[]
  boots: number[]
  /** Ordered completed-item pool; builds slice paths out of it. */
  items: number[]
  spells: [number, number]
  altSpells: [number, number]
  skillOrders: string[][]
}

const ARCHETYPES = {
  marksman: {
    starterItems: [1055, 2003],
    boots: [3006, 3047],
    items: [6672, 3031, 3094, 3036, 3072, 3085, 3153, 3046],
    spells: [SPELLS.flash, SPELLS.heal],
    altSpells: [SPELLS.flash, SPELLS.barrier],
    skillOrders: [['Q', 'W', 'E'], ['Q', 'E', 'W']],
  },
  mage: {
    starterItems: [1056, 2003],
    boots: [3020, 3158],
    items: [6655, 4645, 3089, 3157, 3135, 3116, 3102, 4628],
    spells: [SPELLS.flash, SPELLS.ignite],
    altSpells: [SPELLS.flash, SPELLS.teleport],
    skillOrders: [['Q', 'W', 'E'], ['Q', 'E', 'W']],
  },
  fighter: {
    starterItems: [1054, 2003],
    boots: [3047, 3111],
    items: [3078, 3071, 3053, 6333, 3161, 3074, 3026, 3748],
    spells: [SPELLS.flash, SPELLS.teleport],
    altSpells: [SPELLS.flash, SPELLS.ignite],
    skillOrders: [['Q', 'E', 'W'], ['Q', 'W', 'E']],
  },
  assassin: {
    starterItems: [1055, 2003],
    boots: [3158, 3047],
    items: [6692, 6676, 3142, 6697, 3814, 6695, 3156, 3036],
    spells: [SPELLS.flash, SPELLS.ignite],
    altSpells: [SPELLS.flash, SPELLS.teleport],
    skillOrders: [['Q', 'W', 'E'], ['W', 'Q', 'E']],
  },
  tank: {
    starterItems: [1054, 2003],
    boots: [3047, 3111],
    items: [3068, 3065, 3075, 3110, 3083, 3742, 6665, 3084],
    spells: [SPELLS.flash, SPELLS.teleport],
    altSpells: [SPELLS.flash, SPELLS.ghost],
    skillOrders: [['Q', 'W', 'E'], ['W', 'Q', 'E']],
  },
  enchanter: {
    starterItems: [3865, 2003],
    boots: [3158, 3111],
    items: [6617, 3107, 3222, 3504, 2065, 3190, 3011, 3050],
    spells: [SPELLS.flash, SPELLS.ignite],
    altSpells: [SPELLS.flash, SPELLS.exhaust],
    skillOrders: [['E', 'Q', 'W'], ['Q', 'E', 'W']],
  },
  jungler: {
    starterItems: [1102, 2003],
    boots: [3047, 3158],
    items: [6631, 3071, 3053, 6333, 3074, 3161, 3026, 3814],
    spells: [SPELLS.flash, SPELLS.smite],
    altSpells: [SPELLS.ghost, SPELLS.smite],
    skillOrders: [['Q', 'E', 'W'], ['W', 'Q', 'E']],
  },
} satisfies Record<string, Archetype>

// Rune rows per style — real perk ids so the CDragon-backed rune tree renders
// genuine icons. [keystones, row1, row2, row3].
const STYLE_PERKS: Record<number, [number[], number[], number[], number[]]> = {
  8000: [[8005, 8008, 8010, 8021], [9111, 8009, 9101], [9104, 9105, 9103], [8014, 8017, 8299]],
  8100: [[8112, 8128, 9923], [8126, 8139, 8143], [8136, 8120, 8138], [8135, 8105, 8106]],
  8200: [[8214, 8229, 8230], [8226, 8275, 8224], [8210, 8234, 8233], [8237, 8232, 8236]],
  8300: [[8351, 8360, 8369], [8306, 8304, 8321], [8313, 8352, 8345], [8347, 8410, 8316]],
  8400: [[8437, 8439, 8465], [8446, 8463, 8401], [8429, 8444, 8473], [8451, 8453, 8242]],
}

const STAT_SHARDS = { offense: [5008, 5005, 5007], flex: [5008, 5010, 5001], defense: [5011, 5013, 5001] } as const

// ─── Champion seeds — one row per (champion, position) ──────────────────────

interface ChampionSeed {
  id: number
  position: string
  archetype: keyof typeof ARCHETYPES
  keystone: number
  primaryStyle: number
  secondaryStyle: number
  /** Base win rate (0..1) the generators wobble around. */
  wr: number
  /** Lane pick rate (0..1). */
  pr: number
}

const seed = (
  id: number,
  position: string,
  archetype: keyof typeof ARCHETYPES,
  keystone: number,
  primaryStyle: number,
  secondaryStyle: number,
  wr: number,
  pr: number,
): ChampionSeed => ({ id, position, archetype, keystone, primaryStyle, secondaryStyle, wr, pr })

const CHAMPION_SEEDS: ChampionSeed[] = [
  // TOP
  seed(266, 'TOP', 'fighter', 8010, 8000, 8400, 0.512, 0.081),
  seed(122, 'TOP', 'fighter', 8010, 8000, 8400, 0.523, 0.094),
  seed(114, 'TOP', 'fighter', 8010, 8000, 8100, 0.507, 0.066),
  seed(86, 'TOP', 'fighter', 8010, 8000, 8400, 0.531, 0.087),
  seed(24, 'TOP', 'fighter', 8010, 8000, 8300, 0.516, 0.052),
  seed(39, 'TOP', 'fighter', 8010, 8000, 8400, 0.478, 0.049),
  seed(54, 'TOP', 'tank', 8437, 8400, 8300, 0.527, 0.043),
  seed(516, 'TOP', 'tank', 8437, 8400, 8300, 0.503, 0.038),
  seed(875, 'TOP', 'fighter', 8010, 8000, 8400, 0.519, 0.072),
  seed(17, 'TOP', 'mage', 8128, 8100, 8200, 0.495, 0.041),
  seed(92, 'TOP', 'fighter', 8010, 8000, 8100, 0.487, 0.058),
  seed(887, 'TOP', 'fighter', 8010, 8000, 8200, 0.492, 0.034),
  seed(23, 'TOP', 'fighter', 8008, 8000, 8200, 0.509, 0.029),
  seed(106, 'TOP', 'fighter', 8230, 8200, 8400, 0.514, 0.031),
  // JUNGLE
  seed(64, 'JUNGLE', 'jungler', 8010, 8000, 8100, 0.489, 0.083),
  seed(104, 'JUNGLE', 'jungler', 8008, 8000, 8100, 0.506, 0.061),
  seed(121, 'JUNGLE', 'assassin', 8112, 8100, 8000, 0.521, 0.057),
  seed(245, 'JUNGLE', 'assassin', 8128, 8100, 8200, 0.502, 0.049),
  seed(11, 'JUNGLE', 'jungler', 8008, 8000, 8100, 0.528, 0.062),
  seed(234, 'JUNGLE', 'jungler', 8010, 8000, 8100, 0.497, 0.078),
  seed(233, 'JUNGLE', 'jungler', 8010, 8000, 8100, 0.533, 0.036),
  seed(120, 'JUNGLE', 'jungler', 8230, 8200, 8000, 0.518, 0.033),
  seed(32, 'JUNGLE', 'tank', 8439, 8400, 8300, 0.536, 0.042),
  seed(154, 'JUNGLE', 'tank', 8439, 8400, 8300, 0.524, 0.038),
  seed(113, 'JUNGLE', 'tank', 8439, 8400, 8300, 0.508, 0.027),
  seed(76, 'JUNGLE', 'mage', 8112, 8100, 8200, 0.481, 0.024),
  seed(56, 'JUNGLE', 'jungler', 8128, 8100, 8000, 0.526, 0.045),
  seed(59, 'JUNGLE', 'jungler', 8010, 8000, 8300, 0.493, 0.031),
  // MIDDLE
  seed(103, 'MIDDLE', 'mage', 8112, 8100, 8200, 0.522, 0.089),
  seed(134, 'MIDDLE', 'mage', 8112, 8100, 8300, 0.517, 0.064),
  seed(157, 'MIDDLE', 'fighter', 8008, 8000, 8100, 0.484, 0.096),
  seed(777, 'MIDDLE', 'fighter', 8008, 8000, 8100, 0.498, 0.088),
  seed(238, 'MIDDLE', 'assassin', 8112, 8100, 8200, 0.503, 0.074),
  seed(91, 'MIDDLE', 'assassin', 8112, 8100, 8000, 0.515, 0.043),
  seed(517, 'MIDDLE', 'fighter', 8010, 8000, 8100, 0.491, 0.052),
  seed(268, 'MIDDLE', 'mage', 8229, 8200, 8000, 0.476, 0.037),
  seed(13, 'MIDDLE', 'mage', 8230, 8200, 8300, 0.482, 0.028),
  seed(4, 'MIDDLE', 'mage', 8360, 8300, 8200, 0.511, 0.033),
  seed(8, 'MIDDLE', 'mage', 8230, 8200, 8100, 0.520, 0.031),
  seed(112, 'MIDDLE', 'mage', 8229, 8200, 8300, 0.513, 0.041),
  seed(84, 'MIDDLE', 'assassin', 8112, 8100, 8200, 0.479, 0.061),
  seed(101, 'MIDDLE', 'mage', 8229, 8200, 8300, 0.509, 0.026),
  // BOTTOM
  seed(222, 'BOTTOM', 'marksman', 8008, 8000, 8100, 0.525, 0.104),
  seed(145, 'BOTTOM', 'marksman', 8008, 8000, 8100, 0.501, 0.112),
  seed(202, 'BOTTOM', 'marksman', 8369, 8300, 8100, 0.519, 0.083),
  seed(51, 'BOTTOM', 'marksman', 8008, 8000, 8300, 0.507, 0.078),
  seed(81, 'BOTTOM', 'marksman', 8369, 8300, 8200, 0.488, 0.091),
  seed(119, 'BOTTOM', 'marksman', 8008, 8000, 8100, 0.522, 0.039),
  seed(67, 'BOTTOM', 'marksman', 8008, 8000, 8200, 0.512, 0.057),
  seed(22, 'BOTTOM', 'marksman', 8008, 8000, 8200, 0.516, 0.048),
  seed(360, 'BOTTOM', 'marksman', 8010, 8000, 8100, 0.495, 0.044),
  seed(29, 'BOTTOM', 'marksman', 8008, 8000, 8100, 0.529, 0.036),
  seed(236, 'BOTTOM', 'marksman', 8005, 8000, 8100, 0.499, 0.053),
  seed(18, 'BOTTOM', 'marksman', 8008, 8000, 8300, 0.508, 0.042),
  // UTILITY
  seed(412, 'UTILITY', 'enchanter', 8439, 8400, 8300, 0.497, 0.086),
  seed(89, 'UTILITY', 'tank', 8439, 8400, 8300, 0.518, 0.071),
  seed(111, 'UTILITY', 'tank', 8439, 8400, 8100, 0.511, 0.064),
  seed(117, 'UTILITY', 'enchanter', 8214, 8200, 8300, 0.524, 0.058),
  seed(16, 'UTILITY', 'enchanter', 8214, 8200, 8400, 0.531, 0.043),
  seed(25, 'UTILITY', 'enchanter', 8214, 8200, 8300, 0.506, 0.049),
  seed(53, 'UTILITY', 'tank', 8439, 8400, 8300, 0.514, 0.047),
  seed(555, 'UTILITY', 'assassin', 9923, 8100, 8300, 0.492, 0.053),
  seed(235, 'UTILITY', 'marksman', 8369, 8300, 8200, 0.503, 0.046),
  seed(43, 'UTILITY', 'enchanter', 8214, 8200, 8300, 0.509, 0.039),
  seed(497, 'UTILITY', 'enchanter', 8465, 8400, 8300, 0.512, 0.044),
  seed(432, 'UTILITY', 'enchanter', 8465, 8400, 8300, 0.521, 0.032),
]

const seedsById = new Map<number, ChampionSeed>()
for (const s of CHAMPION_SEEDS) if (!seedsById.has(s.id)) seedsById.set(s.id, s)

// ─── Champion list ───────────────────────────────────────────────────────────

// Total games in the per-position sample pool — sized so pick rates translate
// into a few hundred to a few thousand games per row.
const POOL_GAMES = 42_000

export function tierFor(rankIndex: number, total: number): string {
  const pct = rankIndex / total
  if (pct < 0.12) return 'S'
  if (pct < 0.35) return 'A'
  if (pct < 0.62) return 'B'
  if (pct < 0.86) return 'C'
  return 'D'
}

// Presence-first blend (#971): pick rate and ban rate outweigh win rate.
// Mirrors ChampionTierCalculator's weights closely enough for a plausible
// preview fixture — it doesn't need bit-for-bit parity with the backend's
// percentile-rank + bayesian-shrinkage implementation.
const MOCK_PICK_WEIGHT = 0.45
const MOCK_BAN_WEIGHT = 0.30
const MOCK_WIN_WEIGHT = 0.25

async function mockChampionSummaries(): Promise<ChampionSummaryResponse[]> {
  const patch = await latestShortPatch()

  const rowsByPosition = new Map<string, ChampionSeed[]>()
  for (const s of CHAMPION_SEEDS) {
    const bucket = rowsByPosition.get(s.position) ?? []
    bucket.push(s)
    rowsByPosition.set(s.position, bucket)
  }

  // Percentile-rank each metric within its own lane (not patch-wide), the
  // same normalization the backend uses to avoid a lane-size bias (UTILITY
  // has far fewer champions than MIDDLE, so raw pick rates aren't comparable
  // across lanes).
  const banRateById = new Map(CHAMPION_SEEDS.map(s => [s.id, Math.min(0.85, s.pr * 2.5)]))
  const scoreById = new Map<number, number>()
  const tierById = new Map<number, string>()
  for (const rows of rowsByPosition.values()) {
    const percentileRank = (value: (s: ChampionSeed) => number) => {
      const sorted = [...rows].sort((a, b) => value(a) - value(b))
      const denom = Math.max(1, sorted.length - 1)
      return new Map(sorted.map((s, i) => [s.id, i / denom]))
    }
    const pickPct = percentileRank(s => s.pr)
    const banPct = percentileRank(s => banRateById.get(s.id) ?? 0)
    const winPct = percentileRank(s => s.wr)

    const scored = rows.map(s => ({
      seed: s,
      score: (pickPct.get(s.id) ?? 0) * MOCK_PICK_WEIGHT
        + (banPct.get(s.id) ?? 0) * MOCK_BAN_WEIGHT
        + (winPct.get(s.id) ?? 0) * MOCK_WIN_WEIGHT,
    })).sort((a, b) => b.score - a.score)

    scored.forEach(({ seed, score }, i) => {
      scoreById.set(seed.id, score)
      tierById.set(seed.id, tierFor(i, scored.length))
    })
  }

  return CHAMPION_SEEDS.map((s) => {
    const rng = mulberry32(s.id * 31 + s.position.length)
    const games = Math.max(120, Math.round(s.pr * POOL_GAMES * (0.9 + rng() * 0.2)))
    const archetype = ARCHETYPES[s.archetype]
    return {
      championId: s.id,
      games,
      wins: Math.round(games * s.wr),
      winRate: round3(s.wr),
      pickRate: round3(s.pr),
      lanePlayRate: round3(0.7 + rng() * 0.29),
      trueMainCount: Math.max(3, Math.round(games / 55)),
      // The active patch always has ban data (#920).
      banRate: round3(banRateById.get(s.id) ?? 0),
      tier: tierById.get(s.id) ?? 'B',
      tierScore: round3(scoreById.get(s.id) ?? 0),
      position: s.position,
      patchVersion: patch,
      lastUpdatedAtUtc: new Date().toISOString(),
      topBuild: {
        firstItemId: archetype.items[0]!,
        primaryKeystoneId: s.keystone,
        secondaryStyleId: s.secondaryStyle,
        itemPath: [...archetype.items.slice(0, 4), archetype.boots[0]!],
      },
    }
  })
}

// Homepage overview (#972): a small, pre-sorted slice of the summaries above
// plus the true games-analyzed total, mirroring GET /champions/overview.
// mockChampionSummaries() already represents every ranked row (no
// below-floor/position-less rows exist in this fixture), so summing its games
// is the same "true total" the real endpoint computes from the unfiltered
// aggregate groups.
const OVERVIEW_DEFAULT_LIMIT = 8
const OVERVIEW_MAX_LIMIT = 20

async function mockChampionOverview(query: Record<string, unknown>): Promise<ChampionOverviewResponse> {
  const summaries = await mockChampionSummaries()
  const patch = summaries[0]?.patchVersion ?? await latestShortPatch()

  const requestedLimit = Number.parseInt(String(query.limit ?? ''), 10)
  const limit = Number.isFinite(requestedLimit) && requestedLimit > 0
    ? Math.min(requestedLimit, OVERVIEW_MAX_LIMIT)
    : OVERVIEW_DEFAULT_LIMIT

  const tierRank: Record<string, number> = { S: 0, A: 1, B: 2, C: 3, D: 4 }
  const topRows = [...summaries]
    .sort((a, b) =>
      (tierRank[a.tier] ?? 5) - (tierRank[b.tier] ?? 5)
      || b.games - a.games)
    .slice(0, limit)
    .map(s => ({
      championId: s.championId,
      position: s.position,
      tier: s.tier,
      games: s.games,
      winRate: s.winRate,
      pickRate: s.pickRate,
      banRate: s.banRate,
    }))

  return {
    patchVersion: patch,
    // The mock holds one patch, so its "lifetime" total is that patch's — faking a
    // deeper history would only move a number nothing in dev reads twice.
    gamesAnalyzed: summaries.reduce((acc, s) => acc + s.games, 0),
    topRows,
  }
}

// ─── Champion detail (builds) ────────────────────────────────────────────────

function runePage(
  s: ChampionSeed,
  keystone: number,
  games: number,
  pickRate: number,
  winRate: number,
  rng: () => number,
): BuildRunePage {
  const primary = STYLE_PERKS[s.primaryStyle]!
  const secondary = STYLE_PERKS[s.secondaryStyle]!
  const pick = (row: number[]) => row[Math.floor(rng() * row.length)]!
  // Two secondary perks from two distinct rows, as the game enforces: pick a
  // first row, then offset by 1-2 (mod 3) so the second can never collide.
  const secondaryRows = [secondary[1], secondary[2], secondary[3]] as const
  const firstRow = Math.floor(rng() * 3)
  const secondRow = (firstRow + 1 + Math.floor(rng() * 2)) % 3
  return {
    primaryStyleId: s.primaryStyle,
    primaryKeystoneId: keystone,
    primaryPerk1Id: pick(primary[1]),
    primaryPerk2Id: pick(primary[2]),
    primaryPerk3Id: pick(primary[3]),
    secondaryStyleId: s.secondaryStyle,
    secondaryPerk1Id: pick(secondaryRows[firstRow]!),
    secondaryPerk2Id: pick(secondaryRows[secondRow]!),
    statOffense: STAT_SHARDS.offense[Math.floor(rng() * 3)]!,
    statFlex: STAT_SHARDS.flex[Math.floor(rng() * 3)]!,
    statDefense: STAT_SHARDS.defense[Math.floor(rng() * 3)]!,
    games,
    pickRate: round3(pickRate),
    winRate: round3(winRate),
  }
}

function makeBuild(s: ChampionSeed, variant: 0 | 1, totalGames: number): ChampionBuild {
  const rng = mulberry32(s.id * 97 + variant * 13)
  const archetype = ARCHETYPES[s.archetype]
  const primary = STYLE_PERKS[s.primaryStyle]!
  const keystone = variant === 0 ? s.keystone : primary[0].find(k => k !== s.keystone) ?? s.keystone
  // The dominant build owns ~2/3 of the sample, the alternate the rest.
  const share = variant === 0 ? 0.62 + rng() * 0.08 : 0.2 + rng() * 0.08
  const games = Math.round(totalGames * share)
  const wr = s.wr + (variant === 0 ? 0.004 : -0.011) + rng() * 0.01
  // The alternate build leads with a different first item so the tabs differ.
  const items = variant === 0 ? archetype.items : [archetype.items[1]!, archetype.items[0]!, ...archetype.items.slice(2)]

  const itemSet = (itemIds: number[], shareOf: number, wrDelta: number) => ({
    itemIds,
    games: Math.max(15, Math.round(games * shareOf)),
    pickRate: round3(shareOf),
    winRate: round3(Math.min(0.62, Math.max(0.42, wr + wrDelta))),
  })

  const spellSet = (spells: readonly [number, number], shareOf: number, wrDelta: number) => ({
    spell1Id: spells[0],
    spell2Id: spells[1],
    games: Math.max(15, Math.round(games * shareOf)),
    pickRate: round3(shareOf),
    winRate: round3(Math.min(0.62, Math.max(0.42, wr + wrDelta))),
  })

  const skillSet = (sequence: string[], shareOf: number, wrDelta: number) => ({
    sequence,
    games: Math.max(15, Math.round(games * shareOf)),
    pickRate: round3(shareOf),
    winRate: round3(Math.min(0.62, Math.max(0.42, wr + wrDelta))),
  })

  return {
    firstItemId: items[0]!,
    primaryKeystoneId: keystone,
    games,
    pickRate: round3(share),
    winRate: round3(wr),
    core: {
      itemPath: itemSet(items.slice(0, 4), 0.34, 0.006),
      boots: itemSet([archetype.boots[0]!], 0.71, 0.003),
      starterItems: itemSet(archetype.starterItems, 0.83, 0),
      summonerSpells: spellSet(archetype.spells, 0.88, 0.002),
      skillOrder: skillSet(archetype.skillOrders[0]!, 0.76, 0.004),
      runePage: runePage(s, keystone, Math.round(games * 0.55), 0.55, wr + 0.005, mulberry32(s.id * 7 + variant)),
    },
    variations: {
      boots: [itemSet([archetype.boots[1]!], 0.22, -0.008)],
      starterItems: [itemSet([archetype.starterItems[0]!], 0.11, -0.004)],
      summonerSpells: [spellSet(archetype.altSpells, 0.09, -0.006)],
      skillOrder: [skillSet(archetype.skillOrders[1]!, 0.18, -0.005)],
    },
    buildTree: [
      {
        itemId: items[0]!,
        games,
        wins: Math.round(games * wr),
        pickRate: round3(share),
        children: [1, 2].map(i => ({
          itemId: items[i]!,
          games: Math.round(games * (i === 1 ? 0.55 : 0.3)),
          wins: Math.round(games * (i === 1 ? 0.55 : 0.3) * wr),
          pickRate: round3(i === 1 ? 0.55 : 0.3),
          children: [{
            itemId: items[i + 2]!,
            games: Math.round(games * 0.2),
            wins: Math.round(games * 0.2 * wr),
            pickRate: round3(0.36),
            children: [],
          }],
        })),
      },
    ],
    runePages: [
      runePage(s, keystone, Math.round(games * 0.55), 0.55, wr + 0.005, mulberry32(s.id * 7 + variant)),
      runePage(s, keystone, Math.round(games * 0.24), 0.24, wr - 0.009, mulberry32(s.id * 11 + variant)),
    ],
  }
}

// Share of all-rank games each tier holds, ascending — mirrors the backend
// EloBracket ladder for the dev mock (issue #526). A bare tier reads its own
// share; a `<TIER>_PLUS` filter sums that tier and every tier above it; `ALL`
// (or any unrecognised value) is the full pool. Weights sum to 1.
const ELO_TIER_WEIGHTS: Array<{ tier: string, weight: number }> = [
  { tier: 'IRON', weight: 0.04 },
  { tier: 'BRONZE', weight: 0.13 },
  { tier: 'SILVER', weight: 0.20 },
  { tier: 'GOLD', weight: 0.20 },
  { tier: 'PLATINUM', weight: 0.17 },
  { tier: 'EMERALD', weight: 0.14 },
  { tier: 'DIAMOND', weight: 0.09 },
  { tier: 'MASTER', weight: 0.02 },
  { tier: 'GRANDMASTER', weight: 0.007 },
  { tier: 'CHALLENGER', weight: 0.003 },
]

function resolveEloSlice(filter: string | undefined): { bracket: string, fraction: number } {
  if (!filter || filter.toUpperCase() === 'ALL') return { bracket: 'ALL', fraction: 1 }
  const upper = filter.toUpperCase()
  const andAbove = upper.endsWith('_PLUS')
  const tier = andAbove ? upper.slice(0, -'_PLUS'.length) : upper
  const index = ELO_TIER_WEIGHTS.findIndex(entry => entry.tier === tier)
  if (index < 0) return { bracket: 'ALL', fraction: 1 }
  const included = andAbove ? ELO_TIER_WEIGHTS.slice(index) : [ELO_TIER_WEIGHTS[index]!]
  const fraction = included.reduce((sum, entry) => sum + entry.weight, 0)
  return { bracket: andAbove ? `${tier}_PLUS` : tier, fraction }
}

async function mockChampionDetail(
  id: number,
  position: string | undefined,
  eloBracket: string | undefined,
): Promise<ChampionResponse | null> {
  const s = seedsById.get(id)
  if (!s) return null
  // Mirror the backend: a filtered slice for a lane the champion doesn't play
  // is a 404, and the client falls back to the default (unfiltered) slice.
  if (position && position !== s.position) return null
  const patch = await latestShortPatch()
  const rng = mulberry32(s.id * 31 + s.position.length)
  const allGames = Math.max(120, Math.round(s.pr * POOL_GAMES * (0.9 + rng() * 0.2)))

  // Scope the slice to the requested tier(s): ALL keeps the full pool, a tier
  // (or tier-and-above) takes its share so the rank select visibly changes.
  const { bracket, fraction } = resolveEloSlice(eloBracket)
  const totalGames = Math.round(allGames * fraction)
  return {
    championId: s.id,
    patch,
    position: s.position,
    eloBracket: bracket,
    // `fraction` is exactly the share of all-rank games this slice covers —
    // the same definition the real backend uses for eloCoverage.
    eloCoverage: fraction,
    minSampleMet: totalGames >= 20,
    totalGames,
    totalWins: Math.round(totalGames * s.wr),
    builds: [makeBuild(s, 0, totalGames), makeBuild(s, 1, totalGames)],
  }
}

// ─── Champion insight endpoints ──────────────────────────────────────────────

// Two-patch diff for the champion detail page. Uses the champion's dominant
// build on the newer patch and its alternate on the older one so first item /
// keystone / skill order visibly move (variants 0 and 1 of makeBuild differ),
// exercising the delta badges. availablePatchCount is 2, so the section renders;
// a real single-patch champion returns 1 and the page hides the whole section.
async function mockPatchDiff(
  id: number,
  fromQuery: string | undefined,
  toQuery: string | undefined,
): Promise<ChampionPatchDiffResponse | null> {
  const s = seedsById.get(id)
  if (!s) return null
  const latest = await latestShortPatch()
  const [olderPatch, newerPatch] = trendPatches(latest, 2)
  const totalGames = Math.max(120, Math.round(s.pr * POOL_GAMES))

  const side = (patch: string, variant: 0 | 1): ChampionPatchDiffSide => {
    const build = makeBuild(s, variant, totalGames)
    return {
      patch,
      games: build.games,
      wins: Math.round(build.games * build.winRate),
      winRate: build.winRate,
      itemPath: build.core.itemPath,
      runePage: build.core.runePage,
      skillOrder: build.core.skillOrder,
    }
  }

  const from = side(fromQuery || olderPatch!, 1)
  const to = side(toQuery || newerPatch!, 0)
  const firstItem = (s: ChampionPatchDiffSide) => s.itemPath?.itemIds[0] ?? 0
  return {
    championId: id,
    position: s.position,
    availablePatchCount: 2,
    from,
    to,
    delta: {
      winRateChange: round3(to.winRate - from.winRate),
      firstItemChanged: firstItem(from) !== firstItem(to),
      keystoneChanged: (from.runePage?.primaryKeystoneId ?? 0) !== (to.runePage?.primaryKeystoneId ?? 0),
      skillOrderChanged: (from.skillOrder?.sequence ?? []).join() !== (to.skillOrder?.sequence ?? []).join(),
    },
  }
}

async function mockTrend(id: number): Promise<ChampionTrendResponse | null> {
  const s = seedsById.get(id)
  if (!s) return null
  const latest = await latestShortPatch()
  const rng = mulberry32(s.id * 131)
  return {
    championId: s.id,
    position: s.position,
    // trendPatches returns oldest → newest. Ban history cannot be backfilled
    // (#920), so the mock stages the real shape the frontend has to survive: the
    // older half of the series carries a null banRate and only the recent patches
    // have data. That is what makes the trend chart's ban panel testable both
    // ways without a backend.
    points: trendPatches(latest, 6).map((patch, index, all) => ({
      patch,
      winRate: round3(s.wr + (rng() - 0.5) * 0.03),
      pickRate: round3(Math.max(0.004, s.pr + (rng() - 0.5) * 0.02)),
      banRate: index < all.length - 3
        ? null
        : round3(Math.min(0.85, s.pr * 2.5 + (rng() - 0.5) * 0.04)),
      games: Math.round(s.pr * POOL_GAMES * (0.8 + rng() * 0.4)),
    })),
  }
}

// How win rate slopes with game length per archetype: marksmen/mages scale
// up, assassins/junglers peak early, the rest stay flat-ish.
const SCALING_SLOPE: Record<keyof typeof ARCHETYPES, number> = {
  marksman: 0.05, mage: 0.03, fighter: 0.005, assassin: -0.045,
  tank: 0.015, enchanter: 0.02, jungler: -0.03,
}

async function mockScaling(id: number): Promise<ChampionScalingResponse | null> {
  const s = seedsById.get(id)
  if (!s) return null
  const rng = mulberry32(s.id * 307)
  const slope = SCALING_SLOPE[s.archetype]
  const labels = ['< 25 min', '25–30 min', '30–35 min', '35–40 min', '40+ min']
  const buckets = labels.map((label, bucket) => ({
    bucket,
    label,
    games: Math.round(s.pr * POOL_GAMES * [0.22, 0.28, 0.24, 0.16, 0.1][bucket]!),
    winRate: round3(s.wr + slope * (bucket - 2) + (rng() - 0.5) * 0.012),
  }))
  return {
    championId: s.id,
    position: s.position,
    patch: await latestShortPatch(),
    buckets,
    scalingIndex: round3(buckets[4]!.winRate - buckets[0]!.winRate),
  }
}

async function mockPowerspikes(
  id: number,
  buildFirstItemId: number,
  opponentChampionId = 0,
): Promise<ChampionPowerspikesResponse | null> {
  const s = seedsById.get(id)
  if (!s) return null
  // A matchup slice only has spikes for matches folded since #957 shipped, so an
  // opponent whose id is a multiple of 5 stages the empty one — the state the
  // section is actually in for most pairs on day one, and the one the copy has to
  // survive. It is reachable on purpose, like the matchup page's degraded states.
  if (opponentChampionId > 0 && opponentChampionId % 5 === 0) {
    return { championId: s.id, position: s.position, patch: await latestShortPatch(), events: [] }
  }
  // Spikes are scoped to one core build (#890) and to one lane opponent (#957),
  // so the fixture varies with both: each build starts on its own first item and
  // the rest of the sequence rotates, the way two real builds diverge after the
  // first buy, and the opponent reshuffles the magnitudes on top.
  const rng = mulberry32(s.id * 401 + buildFirstItemId + opponentChampionId * 7)
  // Measured on production, the median champion-vs-opponent pair holds 4 games on
  // a patch (#923). The matchup slice therefore reports counts an order of
  // magnitude below the global one, which is exactly what the section must render
  // honestly rather than hide.
  const gameScale = opponentChampionId > 0 ? 0.02 : 1
  const archetype = ARCHETYPES[s.archetype]
  // One spike per core item: mostly positive, tapering with build order, the
  // odd negative read on late defensive buys. The real endpoint returns the
  // build's core path only, in build order (#1021) — which is what this loop
  // already produces, since it walks the archetype's item sequence.
  const rotation = archetype.items.indexOf(buildFirstItemId)
  const buildItems = rotation > 0
    ? [...archetype.items.slice(rotation), ...archetype.items.slice(0, rotation)]
    : archetype.items
  // Up to 7 bars, not 6: the core path is the first item plus a walk capped at
  // ChampionBuildPathAnalyzer.ItemPathMaxDepth (6), so the real endpoint can return
  // seven — and dev has to be able to show the widest row the layout must survive.
  const events: ChampionPowerspikeEvent[] = buildItems.slice(0, 7).map((itemId, i) => ({
    type: 'item' as const,
    refId: itemId,
    avgMinute: round3(9 + i * 4.6 + rng() * 1.6),
    spikeMagnitude: round3((0.09 - i * 0.022) * (rng() > 0.12 ? 1 : -0.6) + (rng() - 0.5) * 0.01),
    games: Math.max(1, Math.round(s.pr * POOL_GAMES * Math.max(0.08, 0.7 - i * 0.11) * gameScale)),
  }))
  events.push(...[6, 11, 16].map(level => ({
    type: 'level' as const,
    refId: level,
    avgMinute: round3(level === 6 ? 7.5 + rng() : level === 11 ? 16 + rng() * 2 : 26 + rng() * 3),
    spikeMagnitude: round3(0.05 - level * 0.002 + (rng() - 0.5) * 0.01),
    games: Math.max(1, Math.round(s.pr * POOL_GAMES * (level === 16 ? 0.4 : 0.9) * gameScale)),
  })))
  // Items in build order, then the level milestones ascending — the response is a
  // display order now, not a ranking, so sorting it by magnitude here would let the
  // mock pass a component the real payload would break (#1021).
  return {
    championId: s.id,
    position: s.position,
    patch: await latestShortPatch(),
    events,
  }
}

const ROAM_SHARE: Record<string, number> = { TOP: 0.14, JUNGLE: 0.66, MIDDLE: 0.34, BOTTOM: 0.17, UTILITY: 0.46 }

async function mockRoam(id: number): Promise<ChampionRoamResponse | null> {
  const s = seedsById.get(id)
  if (!s) return null
  // JUNGLE has no own lane, so the real backend (ChampionRoamQueryService)
  // returns null roamKp for it — mirror that so junglers render the empty
  // state rather than a fabricated roamer verdict, per the ChampionRoamResponse
  // contract ("null ... for JUNGLE").
  if (s.position === 'JUNGLE') {
    return {
      championId: s.id,
      position: s.position,
      patch: await latestShortPatch(),
      games: 0,
      roamKp5: null,
      roamKp10: null,
      roamKp15: null,
    }
  }
  const rng = mulberry32(s.id * 503)
  const games = Math.max(120, Math.round(s.pr * POOL_GAMES))
  // Cumulative out-of-lane kills + assists per game at each minute mark
  // (@15 ≥ @10 ≥ @5), scaled off the position's roam tendency so supports read
  // as roamers and side lanes stay lane-bound — lining up with the verdict
  // thresholds the component applies to @15.
  const roamBias = ROAM_SHARE[s.position] ?? 0.25
  const roamKp15 = round3(Math.max(0.05, roamBias * 3.0 + (rng() - 0.5) * 0.4))
  const roamKp10 = round3(roamKp15 * (0.55 + rng() * 0.12))
  const roamKp5 = round3(roamKp15 * (0.25 + rng() * 0.1))
  return {
    championId: s.id,
    position: s.position,
    patch: await latestShortPatch(),
    games,
    roamKp5,
    roamKp10,
    roamKp15,
  }
}

async function mockMatchups(id: number): Promise<ChampionMatchups | null> {
  const s = seedsById.get(id)
  if (!s) return null
  const rng = mulberry32(s.id * 601)
  const opponents = CHAMPION_SEEDS.filter(o => o.position === s.position && o.id !== s.id)
  return {
    championId: s.id,
    position: s.position,
    patch: await latestShortPatch(),
    matchups: withMatchupShares(opponents.map((o) => {
      const games = Math.round(40 + rng() * 360)
      const winRate = round3(Math.min(0.6, Math.max(0.4, 0.5 + (s.wr - o.wr) * 1.6 + (rng() - 0.5) * 0.07)))
      // Lane outcome (#919). Only *decided* lanes count, so the sample is always a
      // fraction of `games`; every third opponent is left undecided so the panel's
      // dash path is reachable without a backend. Lane WR tracks game WR loosely —
      // winning lane usually helps — but deliberately not exactly, since the whole
      // point of the column is that the two can disagree.
      const decidedLaneGames = o.id % 3 === 0 ? 0 : Math.round(games * (0.4 + rng() * 0.3))
      const laneWinRate = decidedLaneGames === 0
        ? null
        : round3(Math.min(0.75, Math.max(0.25, winRate + (rng() - 0.5) * 0.18)))
      // Gold gap at 15 (#976), on its own sample and not a slice of the decided
      // lanes: it counts every *judged* lane, evens included, so it is larger than
      // `decidedLaneGames` and survives an opponent whose lanes never decided.
      // Every fifth opponent has no measured gap and every seventh only a handful,
      // so the verdict strip's two shortfall states are both reachable without a
      // backend; the gap itself spans all five bands, which is what the strip is for.
      const goldDiffLaneGames = o.id % 5 === 0
        ? 0
        : o.id % 7 === 0
          ? Math.round(2 + rng() * 6)
          : Math.round(games * (0.5 + rng() * 0.3))
      const averageGoldDiffAt15 = goldDiffLaneGames === 0
        ? null
        : Math.round((s.wr - o.wr) * 9000 + (rng() - 0.5) * 320)
      // XP rides on the same sample but deliberately not on the same sign every
      // time (#1111): roughly a third of matchups get a gap pointing the other
      // way, so the "gold ahead, XP behind" reading the pair exists for is
      // reachable without a backend.
      const averageXpDiffAt15 = averageGoldDiffAt15 === null
        ? null
        : Math.round(averageGoldDiffAt15 * (o.id % 3 === 0 ? -0.4 : 0.55) + (rng() - 0.5) * 260)
      return {
        opponentChampionId: o.id,
        games,
        wins: Math.round(games * winRate),
        winRate,
        laneWinRate,
        decidedLaneGames,
        averageGoldDiffAt15,
        goldDiffLaneGames,
        averageXpDiffAt15,
        xpDiffLaneGames: goldDiffLaneGames,
      }
    })),
  }
}

/**
 * Fills in the two fields the panel *ranks* on, which the rows above cannot know
 * on their own: `playRate` needs the field's total, and the Wilson bounds need to
 * agree with the real ones or the mock would order the best/worst lists
 * differently from production — the one property of this panel worth eyeballing
 * without a backend. Mirrors `RateMath.WilsonInterval`.
 */
function withMatchupShares(
  rows: Omit<ChampionMatchupEntry, 'playRate' | 'winRateLowerBound' | 'winRateUpperBound'>[],
): ChampionMatchupEntry[] {
  const total = rows.reduce((sum, row) => sum + row.games, 0)
  const z = 1.959963984540054
  return rows.map((row) => {
    const n = row.games
    const p = n === 0 ? 0 : row.wins / n
    const denominator = 1 + (z * z) / n
    const centre = (p + (z * z) / (2 * n)) / denominator
    const margin = (z * Math.sqrt((p * (1 - p)) / n + (z * z) / (4 * n * n))) / denominator
    return {
      ...row,
      playRate: total === 0 ? 0 : round3(n / total),
      winRateLowerBound: round3(Math.max(0, centre - margin)),
      winRateUpperBound: round3(Math.min(1, centre + margin)),
    }
  })
}

// Synergy mocks (#922). The panel's whole point is that the ranking value is
// observed *minus* expected, so the mock builds the numbers in that direction:
// draw a synergy, derive the expected rate from the marginals the same way the
// backend does, and add them. Ranking the mocked list by raw win rate would
// therefore give a visibly different order — which is what makes the mock
// useful for eyeballing the panel.
const SYNERGY_COHORT_WIN_RATE = 0.52

function logit(rate: number): number {
  const clamped = Math.min(0.999, Math.max(0.001, rate))
  return Math.log(clamped / (1 - clamped))
}

function sigmoid(logOdds: number): number {
  return 1 / (1 + Math.exp(-logOdds))
}

function expectedWinRate(selfWinRate: number, allyWinRates: number[]): number {
  const cohortLogOdds = logit(SYNERGY_COHORT_WIN_RATE)
  return sigmoid(allyWinRates.reduce(
    (acc, ally) => acc + logit(ally) - cohortLogOdds,
    logit(selfWinRate),
  ))
}

async function mockSynergies(
  id: number,
  partnerPosition?: string,
): Promise<ChampionSynergies | null> {
  const s = seedsById.get(id)
  if (!s) return null

  const rng = mulberry32(s.id * 977)
  const championGames = Math.round(600 + rng() * 2400)
  // Mirrors the backend's max(MinSynergyGames, MinSynergyPlayRate × championGames):
  // with a mocked champion between 600 and 3 000 games the share floor is 6 to 30,
  // so which of the two binds varies by champion — the point of the pair.
  const minGames = Math.max(20, Math.ceil(0.01 * championGames))

  const partners: ChampionSynergyEntry[] = CHAMPION_SEEDS
    .filter(p => p.position !== s.position && (!partnerPosition || p.position === partnerPosition))
    .map((p) => {
      const games = Math.round(15 + rng() * 180)
      const baselineGames = games + Math.round(200 + rng() * 900)
      const baselineWinRate = round3(SYNERGY_COHORT_WIN_RATE + (p.wr - 0.5) * 0.8)
      const expected = expectedWinRate(s.wr, [baselineWinRate])
      const synergy = round3((rng() - 0.45) * 0.11)
      const winRate = round3(Math.min(0.85, Math.max(0.15, expected + synergy)))
      return {
        partnerChampionId: p.id,
        partnerPosition: p.position,
        games,
        wins: Math.round(games * winRate),
        winRate,
        playRate: round3(games / championGames),
        partnerBaselineGames: baselineGames,
        partnerBaselineWinRate: baselineWinRate,
        expectedWinRate: round3(expected),
        synergy: round3(winRate - expected),
      }
    })
    .filter(p => p.games >= minGames)
    .sort((a, b) => b.synergy - a.synergy)

  return {
    championId: s.id,
    position: s.position,
    patch: await latestShortPatch(),
    partnerPosition: partnerPosition ?? null,
    minGames,
    championGames,
    championWinRate: round3(s.wr),
    cohortWinRate: SYNERGY_COHORT_WIN_RATE,
    partners,
  }
}

async function mockTrioSynergies(
  id: number,
  partner: number,
  partnerPosition: string,
): Promise<ChampionTrioSynergies | null> {
  const s = seedsById.get(id)
  const p = seedsById.get(partner)
  if (!s || !p) return null

  const rng = mulberry32(s.id * 31 + partner)
  const minGames = 12
  const pairGames = Math.round(20 + rng() * 160)
  const pairWinRate = round3(0.5 + (rng() - 0.5) * 0.12)
  const partnerBaselineWinRate = round3(SYNERGY_COHORT_WIN_RATE + (p.wr - 0.5) * 0.8)

  // Every completion is drawn from the duo's games, so its sample can never
  // exceed pairGames — the ceiling the panel's copy talks about.
  const completions: ChampionTrioSynergyEntry[] = CHAMPION_SEEDS
    .filter(t => t.position !== s.position && t.position !== partnerPosition)
    .map((t) => {
      const games = Math.round(pairGames * (0.2 + rng() * 0.6))
      const baselineWinRate = round3(SYNERGY_COHORT_WIN_RATE + (t.wr - 0.5) * 0.8)
      const expected = expectedWinRate(s.wr, [partnerBaselineWinRate, baselineWinRate])
      const winRate = round3(Math.min(0.9, Math.max(0.1, expected + (rng() - 0.45) * 0.14)))
      return {
        championId: t.id,
        position: t.position,
        games,
        wins: Math.round(games * winRate),
        winRate,
        baselineGames: games + Math.round(300 + rng() * 900),
        baselineWinRate,
        expectedWinRate: round3(expected),
        synergy: round3(winRate - expected),
      }
    })
    .filter(t => t.games >= minGames)
    .sort((a, b) => b.synergy - a.synergy)

  return {
    championId: s.id,
    position: s.position,
    partnerChampionId: p.id,
    partnerPosition,
    patch: await latestShortPatch(),
    minGames,
    pairGames,
    pairWins: Math.round(pairGames * pairWinRate),
    pairWinRate,
    completions,
  }
}

// Dev-only pools used to push the mocked player's opening choices off the
// mains': variants 0 and 1 of `makeBuild` differ on their first item and skill
// order but happen to share a starter and boots, which would leave the card
// with a single diverging row and most of its copy unexercised.
const MOCK_ALT_STARTERS = [1055, 1054, 1056, 1082]
const MOCK_ALT_BOOTS = [3006, 3047, 3020, 3111, 3158]

function mockDifferentFrom(pool: number[], taken: number | undefined): number {
  return pool.find(candidate => candidate !== taken) ?? pool[0]!
}

/**
 * "<player> vs mains" for the player-scoped champion page. Built from the two build
 * variants `makeBuild` already produces — variant 1 stands in for the player's
 * habits, variant 0 for the mains' — with the starter and boots nudged apart so
 * the fixture shows both diverging and matching rows. Nothing here reaches
 * production (dev mock only).
 */
/**
 * Player-scoped performance score (#918). Mirrors the real payload's shape and
 * its two honest states: a champion the fixture treats as thinly played comes
 * back with the counts and every average null, everything else with a full
 * breakdown. The weights below are the backend's own role profiles
 * (docs/performance-score.md) so the panel orders its rows exactly as it will
 * against the real API; the *values* are deterministic noise, not a
 * re-implementation of the scorer.
 */
function mockPlayerPerformance(
  id: number,
  position: string | null,
  patch: string | null,
): PlayerChampionPerformanceResponse {
  const s = seedsById.get(id)
  const lane = (position ?? s?.position ?? 'MIDDLE').toUpperCase()
  const rng = mulberry32(id * 977 + lane.length * 31)

  const minGames = 5
  const window = 20
  // A deterministic sliver of champions reads as under-sampled so the empty
  // state is reachable without hunting for a real thin account.
  const games = id % 11 === 0 ? id % minGames : 6 + Math.floor(rng() * (window - 6))

  const base: PlayerChampionPerformanceResponse = {
    championId: id,
    position: position ?? null,
    patch: patch ?? null,
    games,
    minGames,
    window,
    averageScore: null,
    bestScore: null,
    worstScore: null,
    topOfTeamRate: null,
    components: [],
  }

  if (games < minGames) return base

  // Backend role profiles, in PERFORMANCE_COMPONENT_KINDS order.
  const WEIGHTS: Record<string, number[]> = {
    TOP: [20, 14, 16, 7, 14, 5, 12, 7, 5],
    JUNGLE: [18, 18, 14, 7, 14, 7, 12, 10, 0],
    MIDDLE: [20, 14, 18, 7, 14, 5, 10, 6, 6],
    BOTTOM: [20, 12, 20, 7, 16, 4, 10, 8, 3],
    UTILITY: [18, 20, 7, 4, 5, 24, 8, 6, 8],
  }
  const weights = WEIGHTS[lane] ?? WEIGHTS.MIDDLE!

  const components = PERFORMANCE_COMPONENT_KINDS.map((kind: PerformanceComponentKind, i) => {
    const weight = weights[i]!
    // MidGame is the component most often absent (short games), so it gets the
    // smaller sample — the "n/N games" note has to be reachable in mock mode.
    const componentGames = kind === 'MidGame'
      ? Math.max(1, Math.round(games * 0.6))
      : kind === 'Laning' || kind === 'Roam'
        ? Math.max(1, Math.round(games * 0.85))
        : games
    return {
      kind,
      weight,
      value: weight === 0 ? null : round3(0.35 + rng() * 0.5),
      games: weight === 0 ? 0 : componentGames,
    }
  })

  const averageScore = Math.round((45 + rng() * 35) * 10) / 10
  return {
    ...base,
    averageScore,
    bestScore: Math.min(100, Math.round(averageScore + 8 + rng() * 12)),
    worstScore: Math.max(0, Math.round(averageScore - 12 - rng() * 15)),
    topOfTeamRate: round3(0.1 + rng() * 0.35),
    components,
  }
}

async function mockPlayerDivergence(id: number): Promise<PlayerBuildDivergenceResponse | null> {
  const s = seedsById.get(id)
  if (!s) return null

  const mainsGames = Math.max(120, Math.round(s.pr * POOL_GAMES))
  const playerGames = 24
  const playerBuild = makeBuild(s, 1, playerGames)
  const mainsBuild = makeBuild(s, 0, mainsGames)

  const mainsStarter = mainsBuild.core.starterItems?.itemIds ?? []
  const mainsBoots = mainsBuild.core.boots?.itemIds ?? []
  const playerStarter = [mockDifferentFrom(MOCK_ALT_STARTERS, mainsStarter[0]), 2003]
  const playerBoots = [mockDifferentFrom(MOCK_ALT_BOOTS, mainsBoots[0])]

  const choice = (
    itemIds: number[],
    skills: string[],
    games: number,
    pickRate: number,
    winRate: number,
  ): BuildChoice => ({ itemIds, skills, games, pickRate: round3(pickRate), winRate: round3(winRate) })

  const row = (
    dimension: BuildDivergence['dimension'],
    playerChoice: BuildChoice,
    mainsChoice: BuildChoice,
  ): BuildDivergence => {
    const diverges = playerChoice.itemIds.join() !== mainsChoice.itemIds.join()
      || playerChoice.skills.join() !== mainsChoice.skills.join()
    const rateOnPlayerChoice = diverges ? 0.09 : mainsChoice.pickRate
    const gamesOnPlayerChoice = Math.round(mainsGames * rateOnPlayerChoice)
    return {
      dimension,
      diverges,
      player: playerChoice,
      mains: mainsChoice,
      mainsGamesOnPlayerChoice: gamesOnPlayerChoice,
      mainsRateOnPlayerChoice: round3(rateOnPlayerChoice),
      // Mirror the backend contract exactly: no games on the player's choice
      // means there is no win rate to report. A mock that invented one here
      // would let the card ship copy the real API can never produce.
      mainsWinRateOnPlayerChoice: gamesOnPlayerChoice === 0 ? null : round3(s.wr - 0.03),
    }
  }

  const dimensions: BuildDivergence[] = [
    row(
      'starterItems',
      choice(playerStarter, [], 17, 0.71, s.wr - 0.02),
      choice(mainsStarter, [], Math.round(mainsGames * 0.68), 0.68, s.wr),
    ),
    row(
      'boots',
      choice(playerBoots, [], 14, 0.58, s.wr - 0.01),
      choice(mainsBoots, [], Math.round(mainsGames * 0.61), 0.61, s.wr),
    ),
    row(
      'itemPath',
      choice((playerBuild.core.itemPath?.itemIds ?? []).slice(0, 3), [], 11, 0.46, s.wr - 0.04),
      choice((mainsBuild.core.itemPath?.itemIds ?? []).slice(0, 3), [], Math.round(mainsGames * 0.52), 0.52, s.wr),
    ),
    row(
      'skillOrder',
      choice([], playerBuild.core.skillOrder?.sequence ?? [], 20, 0.83, s.wr),
      choice([], mainsBuild.core.skillOrder?.sequence ?? [], Math.round(mainsGames * 0.88), 0.88, s.wr),
    ),
  ]

  return {
    championId: s.id,
    patch: await latestShortPatch(),
    position: s.position,
    playerGames,
    mainsGames,
    mainsPlayers: Math.max(1, Math.round(mainsGames / 9)),
    minPlayerGames: 5,
    minMainsGames: 20,
    minSampleMet: true,
    referenceSampleMet: true,
    // Same ordering rule as the backend: what differs first, then by how
    // strongly the mains agree on their own pick.
    dimensions: dimensions.sort((a, b) =>
      Number(b.diverges) - Number(a.diverges) || b.mains.pickRate - a.mains.pickRate),
  }
}

// ─── Truemains leaderboard / search / profiles ──────────────────────────────

const NAME_PREFIXES = ['Kass', 'Vex', 'Luna', 'Drak', 'Zephyr', 'Nox', 'Aurel', 'Milo', 'Rift', 'Umbra', 'Iron', 'Swift', 'Crimson', 'Echo', 'Frost', 'Blaze', 'Storm', 'Nyx', 'Silver', 'Wisp']
const NAME_SUFFIXES = ['smith', 'walker', 'blade', 'main', 'senpai', 'fox', 'wolf', 'heart', 'strike', 'mind']
const REGION_PLATFORMS: Record<RegionSlug, string> = { europe: 'EUW1', americas: 'NA1', korea: 'KR' }

interface MockPlayer {
  row: LeaderboardRowResponse
  /** Dominant lane, used by the leaderboard position filter. */
  position: string
  nameTag: string
}

const DAY_MS = 24 * 60 * 60 * 1000
const PLAYER_COUNT = 120

// Mirrors backend/Core/Truemains/DedicationScore.cs. Duplicated here (like the
// pagination clamping and the OTP threshold above) because the mock has to
// produce a payload the real backend could have produced — see
// docs/dedication-score.md for the formula and the constants.
const DEDICATION_COMMITMENT_WEIGHT = 0.45
const DEDICATION_SPAN_WEIGHT = 0.20
const DEDICATION_VOLUME_WEIGHT = 0.20
const DEDICATION_RECENCY_WEIGHT = 0.15
const DEDICATION_COMMITMENT_FLOOR = 0.12
const DEDICATION_SPAN_TARGET_PATCHES = 6
const DEDICATION_VOLUME_TARGET_GAMES = 200
const DEDICATION_RECENCY_HALF_LIFE_DAYS = 21

function clamp01(value: number): number {
  return Math.min(1, Math.max(0, value))
}

function mockDedication(
  championId: number,
  playRate: number,
  careerGames: number,
  patchSpan: number,
  daysSinceLastGame: number,
): TruemainDedication {
  const commitment = clamp01((playRate - DEDICATION_COMMITMENT_FLOOR) / (1 - DEDICATION_COMMITMENT_FLOOR))
  const span = clamp01(patchSpan / DEDICATION_SPAN_TARGET_PATCHES)
  const volume = clamp01(Math.log(1 + careerGames) / Math.log(1 + DEDICATION_VOLUME_TARGET_GAMES))
  const recency = clamp01(0.5 ** (Math.max(0, daysSinceLastGame) / DEDICATION_RECENCY_HALF_LIFE_DAYS))
  const score = 100 * clamp01(
    DEDICATION_COMMITMENT_WEIGHT * commitment
    + DEDICATION_SPAN_WEIGHT * span
    + DEDICATION_VOLUME_WEIGHT * volume
    + DEDICATION_RECENCY_WEIGHT * recency,
  )

  return {
    score: Math.round(score * 10) / 10,
    championId,
    commitment: round3(commitment),
    span: round3(span),
    volume: round3(volume),
    recency: round3(recency),
    playRate: round3(playRate),
    careerGames,
    patchSpan,
    daysSinceLastGame,
  }
}

function buildPlayers(): MockPlayer[] {
  const players: MockPlayer[] = []
  for (let i = 0; i < PLAYER_COUNT; i++) {
    const rng = mulberry32(i * 1013 + 7)
    const rank = i + 1
    const gameName = i === 0
      ? 'Sheiden'
      : `${NAME_PREFIXES[i % NAME_PREFIXES.length]}${NAME_SUFFIXES[Math.floor(i / NAME_PREFIXES.length) % NAME_SUFFIXES.length]}`
    const tagLine = i === 0 ? '1234' : String(1000 + Math.floor(rng() * 9000))
    const region: RegionSlug = i === 0 ? 'europe' : (['europe', 'europe', 'americas', 'americas', 'korea'] as const)[i % 5]!

    // Monotonic ladder: Challenger → GM → Master → Diamond as rank grows.
    const lp = i === 0
      ? 1247
      : Math.max(0, Math.round(1180 * Math.exp(-i / 38) + (rng() - 0.5) * 30))
    const tier = i === 0 ? 'CHALLENGER' : lp >= 900 ? 'CHALLENGER' : lp >= 500 ? 'GRANDMASTER' : lp >= 60 ? 'MASTER' : 'DIAMOND'

    // 1-3 mains drawn deterministically from the champion seeds.
    const mainCount = 1 + Math.floor(rng() * 3)
    const firstSeedIndex = Math.floor(rng() * CHAMPION_SEEDS.length)
    const mains = Array.from({ length: mainCount }, (_, m) =>
      CHAMPION_SEEDS[(firstSeedIndex + m * 11) % CHAMPION_SEEDS.length]!)
    const games = 180 + Math.floor(rng() * 420)
    const winRate = 0.5 + (0.62 - 0.5) * Math.exp(-i / 45) + (rng() - 0.5) * 0.04
    const wins = Math.round(games * winRate)

    const topChampions = mains.map((m, idx) => {
      // A single-main player is a one-trick: that champion clears the 85%
      // OTP bar, so isOtp rides through and the row shows the OTP badge.
      // Multi-main players spread their play rate and never trip the flag.
      const soloMain = mains.length === 1
      const playRate = soloMain
        ? 0.86 + rng() * 0.1
        : [0.44, 0.21, 0.12][idx]! + rng() * 0.08
      return {
        championId: m.id,
        games: Math.round(games * playRate),
        playRate: round3(playRate),
        isOtp: soloMain,
        primaryKeystoneId: m.keystone,
        secondaryStyleId: m.secondaryStyle,
        firstItemId: ARCHETYPES[m.archetype].items[0]!,
      }
    })

    // Dedication on the signature champion (the top main), with a deterministic
    // history: more patches and fresher games the higher up the ladder a player
    // sits, so the mocked leaderboard reorders visibly under ?sort=dedication.
    const signature = topChampions[0]!
    const dedication = mockDedication(
      signature.championId,
      signature.playRate,
      signature.games,
      1 + Math.floor(rng() * 9),
      Math.floor(rng() * 40),
    )

    players.push({
      position: mains[0]!.position,
      nameTag: `${gameName}-${tagLine}`,
      row: {
        rank,
        identity: {
          gameName,
          tagLine,
          platformId: REGION_PLATFORMS[region],
          profileIconId: 4000 + Math.floor(rng() * 1500),
          summonerLevel: 250 + Math.floor(rng() * 600),
        },
        region,
        ranked: { tier, division: 'I', leaguePoints: lp, score: 10_000 - rank },
        stats: {
          games,
          wins,
          losses: games - wins,
          winRate: round3(winRate),
          kda: round3(1.9 + rng() * 3.4),
        },
        topChampions,
        dedication,
        // Primary is the top main's lane; secondary is the first differing lane
        // among the other mains (null when every main shares one lane), matching
        // the backend's primary/secondary derivation from position share.
        positions: {
          primary: mains[0]!.position,
          secondary: mains.slice(1).map(m => m.position).find(p => p !== mains[0]!.position) ?? null,
        },
      },
    })
  }
  return players
}

let playersCache: MockPlayer[] | null = null
function players(): MockPlayer[] {
  playersCache ??= buildPlayers()
  return playersCache
}

/** Parse + clamp 1-indexed pagination params, mirroring the backend's clamping. */
export function pageParams(
  query: Record<string, unknown>,
  fallbackSize: number,
  maxSize: number,
): { page: number, pageSize: number } {
  const page = Math.max(1, Number.parseInt(String(query.page ?? '1'), 10) || 1)
  // Absent/unparseable pageSize falls back to the default; any parsed number
  // (including an explicit 0) clamps into [1, maxSize] — so pageSize=0 lands on
  // 1 like every other out-of-range value rather than silently reverting to
  // the default.
  const parsedSize = Number.parseInt(String(query.pageSize ?? ''), 10)
  const pageSize = Math.min(maxSize, Math.max(1, Number.isNaN(parsedSize) ? fallbackSize : parsedSize))
  return { page, pageSize }
}

function mockLeaderboard(query: Record<string, unknown>): LeaderboardResponse {
  const { page, pageSize } = pageParams(query, 25, 100)
  const region = typeof query.region === 'string' ? query.region : null
  const position = typeof query.position === 'string' ? query.position : null
  const championId = Number.parseInt(String(query.championId ?? ''), 10) || null
  const otpOnly = query.otpOnly === 'true' || query.otpOnly === true || query.otpOnly === '1'

  let rows = players()
  if (region) rows = rows.filter(p => p.row.region === region)
  if (position) rows = rows.filter(p => p.position === position)
  if (championId) rows = rows.filter(p => p.row.topChampions.some(c => c.championId === championId))
  // otpOnly narrows to one-tricks; with a champion filter it means "OTP of that
  // champion", mirroring the backend's single-row predicate.
  if (otpOnly) {
    rows = championId
      ? rows.filter(p => p.row.topChampions.some(c => c.championId === championId && c.isOtp))
      : rows.filter(p => p.row.topChampions.some(c => c.isOtp))
  }

  // `?sort=dedication` re-ranks on the dedication score, as the backend does
  // (score desc, then a stable tiebreak). Anything else keeps the seeded ladder
  // order, which already stands in for the ranked-standing sort.
  if (query.sort === 'dedication') {
    rows = [...rows].sort((a, b) =>
      (b.row.dedication?.score ?? -1) - (a.row.dedication?.score ?? -1)
      || a.nameTag.localeCompare(b.nameTag))
  }

  const start = (page - 1) * pageSize
  return {
    // Re-rank within the filtered set, as the backend does.
    rows: rows.slice(start, start + pageSize).map((p, i) => ({ ...p.row, rank: start + i + 1 })),
    page,
    pageSize,
    total: rows.length,
  }
}

function mockSearch(q: string): SearchResponse {
  const needle = q.trim().toLowerCase()
  if (needle.length < 2) return { results: [] }
  return {
    results: players()
      .filter(p => p.row.identity.gameName.toLowerCase().includes(needle))
      .slice(0, 8)
      .map(p => ({
        identity: p.row.identity,
        region: p.row.region,
        ranked: p.row.ranked
          ? { tier: p.row.ranked.tier, division: p.row.ranked.division, leaguePoints: p.row.ranked.leaguePoints }
          : null,
        topChampionIds: p.row.topChampions.map(c => c.championId),
        positions: p.row.positions,
      })),
  }
}

function findPlayer(nameTag: string): MockPlayer | undefined {
  const target = nameTag.toLowerCase()
  return players().find(p => p.nameTag.toLowerCase() === target)
}

function mockProfile(player: MockPlayer): ProfileResponse {
  const { row } = player
  const stats = row.stats
  return {
    identity: row.identity,
    ranked: row.ranked && {
      tier: row.ranked.tier,
      division: row.ranked.division,
      leaguePoints: row.ranked.leaguePoints,
      wins: stats.wins,
      losses: stats.losses,
      winRate: stats.winRate,
    },
    mains: row.topChampions.map(c => ({
      championId: c.championId,
      games: c.games,
      playRate: c.playRate,
      primaryPosition: seedsById.get(c.championId)?.position ?? '',
      isOtp: c.isOtp,
      // The mock always describes a freshly-measured account: the retired-sample
      // state (#1216) is exercised on /dev/profile instead, where it can be
      // pinned rather than derived.
      isSampleRetired: false,
      measuredAtUtc: new Date().toISOString(),
    })),
    // Same payload the leaderboard row carries, so the profile card and the
    // leaderboard column agree — exactly as they do against the real backend.
    dedication: row.dedication,
    positions: [
      { position: player.position, games: Math.round(stats.games * 0.78), rate: 0.78 },
      { position: player.position === 'MIDDLE' ? 'TOP' : 'MIDDLE', games: Math.round(stats.games * 0.15), rate: 0.15 },
      { position: player.position === 'BOTTOM' ? 'UTILITY' : 'BOTTOM', games: Math.round(stats.games * 0.07), rate: 0.07 },
    ],
  }
}

function mockRankHistory(player: MockPlayer): RankHistoryResponse {
  const endLp = player.row.ranked?.leaguePoints ?? 0
  const apex = player.row.ranked?.tier !== 'DIAMOND'
  const rng = mulberry32(player.row.rank * 733)
  const now = Date.now()
  const days = 60
  const entries = Array.from({ length: days }, (_, i) => {
    const day = days - 1 - i
    const progress = 1 - day / (days - 1)
    const eased = progress * progress * (3 - 2 * progress)
    const startLp = apex ? Math.max(0, endLp - 320) : 20
    const lp = Math.max(0, Math.round(startLp + (endLp - startLp) * eased + Math.sin(day / 4.1) * 14 + (rng() - 0.5) * 8))
    // Same apex cutoffs as the Sheiden fixture so both dev datasets agree.
    const tier = apex ? apexTierForLp(lp).tier : 'DIAMOND'
    return {
      capturedAtUtc: new Date(now - day * DAY_MS).toISOString(),
      tier,
      division: 'I',
      leaguePoints: !apex ? Math.min(99, lp) : lp,
    }
  })
  return { entries }
}

/**
 * Activity grid (#927). Reproduces the retention asymmetry on purpose: the three
 * match-sourced series only cover the last ~26 days (what `match_participants`
 * still holds in prod), while the patch series carries the player's whole tracked
 * career on their signature champion and sums to the dedication card's
 * `careerGames`. A mock with four equally deep series would hide the one property
 * the card's copy is about.
 */
function mockActivity(player: MockPlayer): TruemainActivityResponse {
  const rng = mulberry32(player.row.rank * 4409)
  const now = Date.now()
  const retainedDays = 26
  const winRate = player.row.stats.winRate ?? 0.5

  interface Game { startUtc: number, win: boolean, championId: number }
  const games: Game[] = []
  const pool = player.row.topChampions.map(champion => champion.championId)
  for (let day = retainedDays - 1; day >= 0; day--) {
    // A rest day every so often, so the day grid carries genuinely empty cells
    // next to the played ones.
    const perDay = rng() < 0.22 ? 0 : 1 + Math.floor(rng() * 4)
    for (let i = 0; i < perDay; i++) {
      games.push({
        startUtc: now - day * DAY_MS + i * 40 * 60 * 1000,
        win: rng() < winRate,
        championId: pool[Math.floor(rng() * pool.length)] ?? pool[0] ?? 157,
      })
    }
  }

  const isoDay = (ms: number) => new Date(ms).toISOString().slice(0, 10)
  const floorDay = (ms: number) => Date.parse(`${isoDay(ms)}T00:00:00.000Z`)
  const floorWeek = (ms: number) => {
    const day = floorDay(ms)
    // ISO weeks start on Monday; getUTCDay() counts from Sunday.
    return day - ((new Date(day).getUTCDay() + 6) % 7) * DAY_MS
  }

  function calendar(floor: (ms: number) => number, stepDays: number): ActivityBucket[] {
    if (games.length === 0) return []
    const totals = new Map<number, { games: number, wins: number }>()
    for (const game of games) {
      const slot = floor(game.startUtc)
      const current = totals.get(slot) ?? { games: 0, wins: 0 }
      totals.set(slot, { games: current.games + 1, wins: current.wins + (game.win ? 1 : 0) })
    }

    const buckets: ActivityBucket[] = []
    const last = floor(now)
    for (let slot = floor(games[0]!.startUtc); slot <= last; slot += stepDays * DAY_MS) {
      const hit = totals.get(slot)
      buckets.push({
        key: isoDay(slot),
        startUtc: new Date(slot).toISOString(),
        games: hit?.games ?? 0,
        wins: hit?.wins ?? 0,
        // Null, never 0, on an untouched slot — the whole point of the grid.
        winRate: hit ? hit.wins / hit.games : null,
        championId: null,
      })
    }
    return buckets
  }

  function matchSeries(mode: 'game' | 'day' | 'week', buckets: ActivityBucket[]): ActivitySeries {
    const total = buckets.reduce((sum, bucket) => sum + bucket.games, 0)
    const wins = buckets.reduce((sum, bucket) => sum + bucket.wins, 0)
    return {
      mode,
      source: 'matches',
      scope: 'allChampions',
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

  const gameBuckets: ActivityBucket[] = games.slice(-60).map(game => ({
    key: `MOCK_${game.startUtc}`,
    startUtc: new Date(game.startUtc).toISOString(),
    games: 1,
    wins: game.win ? 1 : 0,
    winRate: game.win ? 1 : 0,
    championId: game.championId,
  }))

  // The patch series must total the dedication card's careerGames on the same
  // champion, which is the invariant the real endpoint guarantees by reading the
  // very rows that card sums.
  const dedication = player.row.dedication
  const patchSpan = Math.max(1, dedication?.patchSpan ?? 1)
  const careerGames = Math.max(patchSpan, dedication?.careerGames ?? patchSpan)
  const patchBuckets: ActivityBucket[] = Array.from({ length: patchSpan }, (_, index) => {
    // Distribute the career evenly and hand the remainder to the newest patch so
    // the sum is exact rather than approximately right.
    const share = Math.floor(careerGames / patchSpan)
    const count = index === patchSpan - 1 ? careerGames - share * (patchSpan - 1) : share
    const wins = Math.round(count * Math.min(0.85, Math.max(0.15, winRate + (rng() - 0.5) * 0.2)))
    return {
      key: `15.${index + 1}`,
      startUtc: null,
      games: count,
      wins,
      winRate: count === 0 ? null : wins / count,
      championId: null,
    }
  })
  const patchTotal = patchBuckets.reduce((sum, bucket) => sum + bucket.games, 0)
  const patchWins = patchBuckets.reduce((sum, bucket) => sum + bucket.wins, 0)

  return {
    game: matchSeries('game', gameBuckets),
    day: matchSeries('day', calendar(floorDay, 1)),
    week: matchSeries('week', calendar(floorWeek, 7)),
    patch: {
      mode: 'patch',
      source: 'aggregates',
      scope: 'champion',
      championId: dedication?.championId ?? null,
      retentionBounded: false,
      coverageFromUtc: null,
      coverageToUtc: null,
      buckets: patchBuckets,
      games: patchTotal,
      wins: patchWins,
      winRate: patchTotal === 0 ? null : patchWins / patchTotal,
    },
  }
}

function mockMatches(player: MockPlayer, query: Record<string, unknown>): MatchSummariesResponse {
  const { page, pageSize } = pageParams(query, 20, 50)
  const total = 46
  const now = Date.now()
  const pool = CHAMPION_SEEDS

  // Generate exactly the rows this page holds (the last page is short).
  const start = (page - 1) * pageSize
  const count = Math.max(0, Math.min(pageSize, total - start))
  const matches = Array.from({ length: count }, (_, i): MatchSummaryResponse => {
    const index = start + i
    const rng = mulberry32(player.row.rank * 8887 + index * 271)
    const main = player.row.topChampions[Math.floor(rng() * player.row.topChampions.length)]!
    const mainSeed = seedsById.get(main.championId)!
    const archetype = ARCHETYPES[mainSeed.archetype]
    const win = rng() < (player.row.stats.winRate ?? 0.5)
    const kills = Math.floor(rng() * 12)
    const deaths = Math.floor(rng() * 8)
    const assists = Math.floor(rng() * 14)
    const duration = 1350 + Math.floor(rng() * 1100)

    // Performance score, placement and the MVP/ACE accolade, kept mutually
    // consistent: the accolade is "placement 1 on your own side", so it can
    // only fire on a top-3 overall game. The real API derives all three from
    // one ranking (docs/performance-score.md); a mock with three independent
    // dice would show a crown on a 34 and make the panel look broken.
    const perf = 28 + Math.floor(rng() * 62)
    const placement = 1 + Math.floor((100 - perf) / 100 * 10)
    const topOfTeam = placement <= 3

    // 10 participants: self + 9 others drawn from the champion pool, split 5v5.
    const selfTeam = rng() < 0.5 ? 100 : 200
    const selfIndex = player.row.rank - 1
    const participants = Array.from({ length: 10 }, (_, slot) => {
      // Skip the current player's own index so self never shows up a second
      // time among the "others".
      let otherIndex = (player.row.rank + slot * 17 + index) % PLAYER_COUNT
      if (otherIndex === selfIndex) otherIndex = (otherIndex + 1) % PLAYER_COUNT
      const other = players()[otherIndex]!
      return {
        championId: slot === 0 ? main.championId : pool[(index * 7 + slot * 13) % pool.length]!.id,
        teamId: slot < 5 ? selfTeam : selfTeam === 100 ? 200 : 100,
        // Slots 0-4 / 5-9 are each a full team in role order, so slot % 5
        // yields a valid one-of-each position assignment per side.
        position: (['TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY'] as const)[slot % 5]!,
        gameName: slot === 0 ? player.row.identity.gameName : other.row.identity.gameName,
        tagLine: slot === 0 ? player.row.identity.tagLine : other.row.identity.tagLine,
      }
    })

    return {
      matchId: `EUW1_${7_100_000_000 + player.row.rank * 10_000 + index}`,
      queueId: 420,
      gameMode: 'CLASSIC',
      gameStartTimeUtc: new Date(now - (index * 11 + 3) * 60 * 60 * 1000).toISOString(),
      gameDurationSeconds: duration,
      self: {
        championId: main.championId,
        championLevel: 12 + Math.floor(rng() * 7),
        summoner1Id: archetype.spells[0],
        summoner2Id: archetype.spells[1],
        primaryStyleId: mainSeed.primaryStyle,
        subStyleId: mainSeed.secondaryStyle,
        keystoneId: mainSeed.keystone,
        kills,
        deaths,
        assists,
        cs: Math.round(duration / 60 * (5.5 + rng() * 3.5)),
        killParticipation: round3(Math.min(0.9, 0.3 + rng() * 0.5)),
        items: [...archetype.items.slice(0, 5), archetype.boots[0]!],
        trinketItemId: 3364,
        teamId: selfTeam,
        position: (['TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY'] as const)[index % 5]!,
        win,
        lpDelta: win ? 14 + Math.floor(rng() * 12) : -(12 + Math.floor(rng() * 12)),
        // Correlated with the accolade below rather than independent, so the
        // mock never renders a crown next to a mediocre score — the real API
        // derives both from the same ranking.
        performanceScore: perf,
        placement,
        isMvp: win && topOfTeam,
        isAce: !win && topOfTeam,
      },
      participants,
    }
  })

  return { matches, page, pageSize, total }
}

// ─── Router ──────────────────────────────────────────────────────────────────

/**
 * Truthy iff the dev mock is enabled for this process. Matches an explicit
 * allowlist rather than `Boolean(...)` so falsy-looking opt-outs like
 * `NUXT_DEV_MOCK_API=0` or `=false` disable it (a bare `Boolean('0')` is
 * `true`), keeping the documented `=1` opt-in symmetric with env-managed
 * setups that toggle the flag by value.
 */
export function devApiMockEnabled(): boolean {
  const flag = process.env.NUXT_DEV_MOCK_API?.toLowerCase()
  return flag === '1' || flag === 'true'
}

/**
 * `decodeURIComponent` that returns `undefined` on a malformed `%` sequence
 * (e.g. `foo%2`) instead of throwing a `URIError` — the caller treats that as
 * an unknown segment (clean 404) rather than letting it bubble up as a generic
 * Nitro 500.
 */
/**
 * Whether a Riot ID is well-formed, mirroring `NameTagParser.TryParseRiotId`:
 * the typed `Name#TAG` form or the `Name-TAG` slug, both halves non-empty, at
 * most 64 characters. Deliberately *not* the app's stricter `parseRiotId`
 * (which requires the `#`) — the API accepts the slug form, and a mock that
 * rejected it would invent a divergence rather than remove one.
 */
function wellFormedRiotId(riotId: string): boolean {
  const trimmed = riotId.trim()
  if (!trimmed || riotId.length > 64) return false

  const hash = trimmed.indexOf('#')
  if (hash < 0) {
    // Slug fallback: split on the last '-', so game names may contain hyphens.
    const dash = trimmed.lastIndexOf('-')
    return dash > 0 && dash < trimmed.length - 1
  }

  const gameName = trimmed.slice(0, hash).trim()
  const tagLine = trimmed.slice(hash + 1).trim()
  return Boolean(gameName) && Boolean(tagLine) && !tagLine.includes('#')
}

/**
 * Account-vs-mains head-to-head (#528). Mirrors the backend's database-only
 * contract, including the parts that only differ in edge cases — a mock that
 * contradicts the contract is worse than none, because it makes a wrong
 * frontend look right locally:
 *
 * - A Riot ID that is not well-formed is a **400** (the controller validates
 *   before the service ever runs); a well-formed one we hold no player for is
 *   a **200** carrying `UNKNOWN_ACCOUNT`.
 * - `UNKNOWN_TARGET` keeps the `player` column populated — only the yardstick
 *   is missing. Every other field is derived rather than asserted, so the
 *   invariants the read model documents (`winRate = wins / games`,
 *   `sampleMet = games >= minGames`, `status` following from both sides)
 *   cannot drift out of the mock the way a hardcoded `status: 'OK'` did.
 */
async function mockMainsComparison(
  id: number,
  account: string | undefined,
  main: string | undefined,
  position: string | undefined,
  patch: string | undefined,
): Promise<ChampionMainsComparison | null> {
  const s = seedsById.get(id)
  if (!s) return null

  if (!account || !wellFormedRiotId(account)) {
    throw createError({ statusCode: 400, statusMessage: 'account must be a Riot ID of the form Name#TAG (dev mock)' })
  }
  if (main && !wellFormedRiotId(main)) {
    throw createError({ statusCode: 400, statusMessage: 'main must be a Riot ID of the form Name#TAG (dev mock)' })
  }

  // Mirrors ChampionsList:MinComparisonGames, whose default is 5.
  const minGames = 5
  const base = {
    championId: id,
    // The endpoint echoes the *normalised* patch it resolved (major.minor), or
    // null when the caller pinned none.
    patch: patch ? patch.split('.').slice(0, 2).join('.') : null,
    position: position ?? null,
    minGames,
  }

  // The mock players are keyed on the `Name-TAG` slug; the endpoint takes the
  // typed `Name#TAG` form, so normalise before the lookup.
  const player = findPlayer(account.replace('#', '-'))
  if (!player) return { ...base, status: 'UNKNOWN_ACCOUNT', player: null, mains: null }

  const side = (seed: number, identity: ProfileIdentity | null, players: number): ChampionComparisonSide => {
    const rng = mulberry32(seed)
    const games = 6 + Math.floor(rng() * 60) + (players > 1 ? 200 : 0)
    const deaths = round3(3 + rng() * 3)
    const kills = round3(4 + rng() * 5)
    const assists = round3(4 + rng() * 5)
    const goldPerMin = Math.round(360 + rng() * 120)
    // Derive wins first, then the rate from it, so the rendered win rate always
    // agrees with the rendered W/games — the backend divides the same way.
    const wins = Math.round(games * (0.45 + rng() * 0.15))
    return {
      identity,
      players,
      games,
      wins,
      winRate: round3(wins / games),
      kills,
      deaths,
      assists,
      kda: round3((kills + assists) / deaths),
      csPerMin: round3(5.5 + rng() * 3),
      goldPerMin,
      // Mock games are a flat 30 minutes, so per-game gold follows the rate.
      goldPerGame: Math.round(goldPerMin * 30),
      sampleMet: games >= minGames,
    }
  }

  const playerSide = side(s.id * 907 + 11, player.row.identity, 1)

  // A named target we don't hold: the player column stays populated, matching
  // ChampionMainsComparisonQueryService — only the yardstick is missing.
  const target = main ? findPlayer(main.replace('#', '-')) : undefined
  if (main && !target) {
    return { ...base, status: 'UNKNOWN_TARGET', player: playerSide, mains: null }
  }

  const mainsSide = target
    ? side(s.id * 911 + 13, target.row.identity, 1)
    : side(s.id * 919 + 17, null, 12)

  return {
    ...base,
    status: playerSide.sampleMet && mainsSide.sampleMet ? 'OK' : 'INSUFFICIENT_SAMPLE',
    player: playerSide,
    mains: mainsSide,
  }
}

function safeDecodeURIComponent(value: string): string | undefined {
  try {
    return decodeURIComponent(value)
  }
  catch {
    return undefined
  }
}

/**
 * Resolve a mock payload for a backend API request, or `undefined` when the
 * path isn't one the mock serves (the caller then proxies as usual).
 *
 * @param path - Backend-relative path, no `/api` prefix or query string
 *   (e.g. `/champions/64/matchups`).
 * @param query - Parsed query params.
 */
// ─── Composition build (builder page) ────────────────────────────────────────
// Deterministic per champion; the POST body is deliberately ignored — the
// builder page only needs a realistic payload to render, and a body-sensitive
// mock would just re-implement the backend scorer.

async function mockCompositionBuild(id: number, body?: unknown): Promise<CompositionBuildResponse> {
  // Unseeded champions still get a plausible payload (fighter defaults): the
  // builder lets you pick any champion, unlike the seeded directory pages.
  const s = seedsById.get(id) ?? seed(id, 'MIDDLE', 'fighter', 8010, 8000, 8400, 0.508, 0.04)
  const patch = await latestShortPatch()
  const rng = mulberry32(s.id * 131 + 7)
  const archetype = ARCHETYPES[s.archetype]

  // Mirror the backend's matchup requirement from the request body: the role
  // opponent is the enemy slot at the player's own position. A deterministic
  // sliver of opponents (id % 23 === 0, e.g. Kayn 141… pick around to find
  // one) reads as "never recorded" so the fallback path can be eyeballed.
  const draft = (body ?? {}) as {
    position?: string
    allies?: Array<{ championId?: number, position?: string }>
    enemies?: Array<{ championId?: number, position?: string }>
  }
  const position = typeof draft.position === 'string' ? draft.position.toUpperCase() : s.position
  const roleOpponent = (draft.enemies ?? []).find(slot =>
    typeof slot.position === 'string' && slot.position.toUpperCase() === position)
  const matchupRequested = roleOpponent?.championId !== undefined
  const matchupFound = !matchupRequested || (roleOpponent!.championId! % 23 !== 0)
  const slotCount = (draft.allies?.length ?? 0) + (draft.enemies?.length ?? 0)

  // Fewer games as the draft gets more constrained, so the thin-data state is
  // reachable in mock mode (the real API returns the true selected count).
  // A second deterministic sliver of opponents (id % 17 === 0, e.g. Teemo)
  // comes back as a barely-recorded matchup, so the matchup page's thin-sample
  // fallback (#921) is reachable without filling all eight draft slots.
  const thinMatchup = matchupRequested && matchupFound && roleOpponent!.championId! % 17 === 0
  const games = thinMatchup ? 3 : Math.max(6, Math.round(100 - slotCount * 11))
  const wins = Math.round(games * Math.min(0.6, s.wr + rng() * 0.02))
  const set = (itemIds: number[], shareOf: number) => ({
    itemIds,
    games: Math.max(1, Math.min(games, Math.round(games * shareOf))),
    pickRate: round3(shareOf),
    winRate: round3(Math.min(0.62, Math.max(0.42, s.wr + (rng() - 0.5) * 0.08))),
  })

  if (!matchupFound) {
    return {
      championId: s.id,
      position,
      patch,
      eloBracket: 'all',
      matchupRequested,
      matchupFound,
      confidence: {
        sampleSize: 0,
        candidatePoolSize: 2400 + Math.round(rng() * 2000),
        truemainGameCount: 0,
        maxPossibleScore: 10 + slotCount * 3,
        meanSimilarity: 0,
      },
      // Nothing sampled, so nothing to judge: the strip must show em dashes here,
      // never a 0% lane.
      lane: {
        measuredGames: 0,
        decidedGames: 0,
        winRate: null,
        averageGoldDiffAt15: null,
        averageXpDiffAt15: null,
      },
      build: {
        gamesConsidered: 0,
        wins: 0,
        runePage: null,
        starterItems: null,
        boots: null,
        corePath: null,
        summonerSpells: null,
        skillOrder: null,
        firstItemId: 0,
        buildTree: [],
      },
    }
  }

  const treeItems = archetype.items
  const treeChild = (index: number, share: number) => ({
    itemId: treeItems[index]!,
    games: Math.round(games * share),
    wins: Math.round(games * share * s.wr),
    pickRate: round3(share),
    children: treeItems[index + 2] === undefined
      ? []
      : [{
          itemId: treeItems[index + 2]!,
          games: Math.round(games * share * 0.4),
          wins: Math.round(games * share * 0.4 * s.wr),
          pickRate: round3(0.4),
          children: [],
        }],
  })

  return {
    championId: s.id,
    position,
    patch,
    eloBracket: 'all',
    matchupRequested,
    matchupFound,
    confidence: {
      sampleSize: games,
      candidatePoolSize: 2400 + Math.round(rng() * 2000),
      truemainGameCount: Math.round(games * (0.6 + rng() * 0.35)),
      maxPossibleScore: 10 + slotCount * 3,
      // More filled slots read as a tighter (lower) mean similarity, so the
      // confidence strip visibly reacts to draft edits in mock mode.
      meanSimilarity: round3(Math.max(0.18, 0.75 - slotCount * 0.05 + rng() * 0.1)),
    },
    // Judged over the sampled games (#1117): fewer are measurable than sampled (a
    // game that ended before 15 min is not a judgeable lane), fewer still decided,
    // and the XP gap points the other way one champion in three so the "gold ahead,
    // XP behind" reading is reachable without a backend.
    lane: (() => {
      const measured = Math.max(1, Math.round(games * (0.7 + rng() * 0.25)))
      const decided = Math.max(0, Math.round(measured * (0.45 + rng() * 0.3)))
      const gold = Math.round((s.wr - 0.5) * 4200 + (rng() - 0.5) * 400)
      return {
        measuredGames: measured,
        decidedGames: decided,
        winRate: decided === 0 ? null : round3(Math.min(0.85, Math.max(0.15, s.wr + (rng() - 0.5) * 0.2))),
        averageGoldDiffAt15: gold,
        averageXpDiffAt15: Math.round(gold * (s.id % 3 === 0 ? -0.4 : 0.55) + (rng() - 0.5) * 260),
      }
    })(),
    build: {
      gamesConsidered: games,
      wins,
      runePage: runePage(s, s.keystone, Math.round(games * 0.46), 0.46, s.wr + 0.008, rng),
      starterItems: set(archetype.starterItems, 0.6 + rng() * 0.1),
      boots: set([archetype.boots[0]!], 0.55 + rng() * 0.1),
      corePath: set(archetype.items.slice(0, 3), 0.38 + rng() * 0.1),
      summonerSpells: {
        spell1Id: archetype.spells[0],
        spell2Id: archetype.spells[1],
        games: Math.round(games * 0.9),
        pickRate: 0.9,
        winRate: round3(s.wr),
      },
      skillOrder: {
        sequence: [...archetype.skillOrders[0]!],
        games: Math.round(games * 0.72),
        pickRate: 0.72,
        winRate: round3(s.wr + 0.004),
      },
      firstItemId: treeItems[0]!,
      buildTree: [treeChild(1, 0.55), treeChild(2, 0.3)].filter(node => node.itemId !== undefined),
    },
  }
}

/**
 * Games the composition recommendation was computed from (#940), paged. Mirrors
 * `mockCompositionBuild`'s matchup/games-count logic exactly (same seed, same
 * draft-derived `games`) so a drawer opened right after a recommendation loads
 * always lists a sample consistent with the confidence strip that opened it.
 * Games at even indices are attributed to mains (roughly matching the
 * `truemainGameCount` share the recommendation reports) and lead the page, per
 * the real selection's mains-first order.
 */
async function mockCompositionBuildGames(id: number, body: unknown, query: Record<string, unknown>): Promise<CompositionBuildGamesResponse> {
  const s = seedsById.get(id) ?? seed(id, 'MIDDLE', 'fighter', 8010, 8000, 8400, 0.508, 0.04)
  const patch = await latestShortPatch()
  const rng = mulberry32(s.id * 131 + 7)
  const archetype = ARCHETYPES[s.archetype]

  const draft = (body ?? {}) as {
    position?: string
    allies?: Array<{ championId?: number, position?: string }>
    enemies?: Array<{ championId?: number, position?: string }>
  }
  const position = typeof draft.position === 'string' ? draft.position.toUpperCase() : s.position
  const roleOpponent = (draft.enemies ?? []).find(slot =>
    typeof slot.position === 'string' && slot.position.toUpperCase() === position)
  const matchupRequested = roleOpponent?.championId !== undefined
  const matchupFound = !matchupRequested || (roleOpponent!.championId! % 23 !== 0)
  const slotCount = (draft.allies?.length ?? 0) + (draft.enemies?.length ?? 0)
  const thinMatchup = matchupRequested && matchupFound && roleOpponent!.championId! % 17 === 0
  const total = matchupFound ? (thinMatchup ? 3 : Math.max(6, Math.round(100 - slotCount * 11))) : 0
  const maxPossibleScore = 10 + slotCount * 3

  const { page, pageSize } = pageParams(query, 10, 25)
  const start = (page - 1) * pageSize
  const count = Math.max(0, Math.min(pageSize, total - start))
  const now = Date.now()

  const games: CompositionGame[] = Array.from({ length: count }, (_, i): CompositionGame => {
    const index = start + i
    const gameRng = mulberry32(s.id * 733 + index * 197)
    const isTruemain = index % 2 === 0
    const win = gameRng() < Math.min(0.6, s.wr + gameRng() * 0.02)
    const duration = 1350 + Math.floor(gameRng() * 1100)
    const perf = 28 + Math.floor(gameRng() * 62)
    const placement = 1 + Math.floor((100 - perf) / 100 * 10)
    const topOfTeam = placement <= 3
    const selfTeam = gameRng() < 0.5 ? 100 : 200
    const pilotIndex = (s.id * 7 + index * 11) % PLAYER_COUNT
    const pilot = players()[pilotIndex]!
    const pool = CHAMPION_SEEDS

    const participants = Array.from({ length: 10 }, (_, slot) => {
      let otherIndex = (pilotIndex + slot * 17 + index) % PLAYER_COUNT
      if (otherIndex === pilotIndex) otherIndex = (otherIndex + 1) % PLAYER_COUNT
      const other = players()[otherIndex]!
      return {
        championId: slot === 0 ? s.id : pool[(index * 7 + slot * 13) % pool.length]!.id,
        teamId: slot < 5 ? selfTeam : selfTeam === 100 ? 200 : 100,
        position: (['TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY'] as const)[slot % 5]!,
        gameName: slot === 0 ? pilot.row.identity.gameName : other.row.identity.gameName,
        tagLine: slot === 0 ? pilot.row.identity.tagLine : other.row.identity.tagLine,
      }
    })

    const match: MatchSummaryResponse = {
      matchId: `EUW1_COMPG_${s.id}_${index}`,
      queueId: 420,
      gameMode: 'CLASSIC',
      gameStartTimeUtc: new Date(now - (index * 13 + 5) * 60 * 60 * 1000).toISOString(),
      gameDurationSeconds: duration,
      self: {
        championId: s.id,
        championLevel: 12 + Math.floor(gameRng() * 7),
        summoner1Id: archetype.spells[0],
        summoner2Id: archetype.spells[1],
        primaryStyleId: s.primaryStyle,
        subStyleId: s.secondaryStyle,
        keystoneId: s.keystone,
        kills: Math.floor(gameRng() * 12),
        deaths: Math.floor(gameRng() * 8),
        assists: Math.floor(gameRng() * 14),
        cs: Math.round(duration / 60 * (5.5 + gameRng() * 3.5)),
        killParticipation: round3(Math.min(0.9, 0.3 + gameRng() * 0.5)),
        items: [...archetype.items.slice(0, 5), archetype.boots[0]!],
        trinketItemId: 3364,
        teamId: selfTeam,
        position,
        win,
        lpDelta: win ? 14 + Math.floor(gameRng() * 12) : -(12 + Math.floor(gameRng() * 12)),
        performanceScore: perf,
        placement,
        isMvp: win && topOfTeam,
        isAce: !win && topOfTeam,
      },
      participants,
    }

    return {
      // Similarity descends within the page, mains-first ties broken the same
      // way the real selection breaks them: index order.
      score: Math.max(0, maxPossibleScore - index),
      isTruemain,
      pilot: {
        gameName: pilot.row.identity.gameName,
        tagLine: pilot.row.identity.tagLine,
        profileIconId: pilot.row.identity.profileIconId,
      },
      match,
    }
  })

  return {
    championId: s.id,
    position,
    patch,
    page,
    pageSize,
    total,
    maxPossibleScore,
    games,
  }
}

export async function resolveDevApiMock(
  path: string,
  query: Record<string, unknown>,
  body?: unknown,
): Promise<unknown | undefined> {
  if (path === '/champions') return mockChampionSummaries()
  if (path === '/champions/overview') return mockChampionOverview(query)

  const compositionGamesMatch = path.match(/^\/champions\/(\d+)\/composition-build\/games$/)
  if (compositionGamesMatch) return mockCompositionBuildGames(Number(compositionGamesMatch[1]), body, query)

  const compositionMatch = path.match(/^\/champions\/(\d+)\/composition-build$/)
  if (compositionMatch) return mockCompositionBuild(Number(compositionMatch[1]), body)

  // Two segments deep, so it has to be matched before the single-segment
  // champion route below (whose regex would otherwise not match at all).
  const trioMatch = path.match(/^\/champions\/(\d+)\/synergies\/trios$/)
  if (trioMatch) {
    return mockTrioSynergies(
      Number(trioMatch[1]),
      Number(query.partner) || 0,
      typeof query.partnerPosition === 'string' ? query.partnerPosition : '',
    )
  }

  const championMatch = path.match(/^\/champions\/(\d+)(?:\/([a-z-]+))?$/)
  if (championMatch) {
    const id = Number(championMatch[1])
    const sub = championMatch[2]
    const position = typeof query.position === 'string' && query.position ? query.position : undefined
    const eloBracket = typeof query.eloBracket === 'string' && query.eloBracket ? query.eloBracket : undefined
    const payload = await (
      sub === undefined ? mockChampionDetail(id, position, eloBracket)
      : sub === 'trend' ? mockTrend(id)
      : sub === 'patch-diff' ? mockPatchDiff(id, typeof query.from === 'string' ? query.from : undefined, typeof query.to === 'string' ? query.to : undefined)
      : sub === 'scaling' ? mockScaling(id)
      : sub === 'powerspikes' ? mockPowerspikes(
          id,
          Number(query.buildFirstItemId) || 0,
          Number(query.opponentChampionId) || 0,
        )
      : sub === 'roam' ? mockRoam(id)
      : sub === 'matchups' ? mockMatchups(id)
      : sub === 'synergies' ? mockSynergies(
          id,
          typeof query.partnerPosition === 'string' && query.partnerPosition ? query.partnerPosition : undefined,
        )
      : sub === 'mains-comparison' ? mockMainsComparison(
          id,
          typeof query.account === 'string' ? query.account : undefined,
          typeof query.main === 'string' ? query.main : undefined,
          position,
          typeof query.patch === 'string' && query.patch ? query.patch : undefined,
        )
      : Promise.resolve(undefined))
    if (payload === undefined) return undefined
    if (payload === null) {
      throw createError({ statusCode: 404, statusMessage: 'No data for this champion (dev mock)' })
    }
    return payload
  }

  if (path === '/truemains') return mockLeaderboard(query)
  if (path === '/truemains/search') return mockSearch(typeof query.q === 'string' ? query.q : '')

  const playerMatch = path.match(/^\/truemains\/([^/]+)\/(profile|rank-history|activity|matches)$/)
  if (playerMatch) {
    const name = safeDecodeURIComponent(playerMatch[1]!)
    const player = name === undefined ? undefined : findPlayer(name)
    if (!player) throw createError({ statusCode: 404, statusMessage: 'Unknown truemain (dev mock)' })
    if (playerMatch[2] === 'profile') return mockProfile(player)
    if (playerMatch[2] === 'rank-history') return mockRankHistory(player)
    if (playerMatch[2] === 'activity') return mockActivity(player)
    return mockMatches(player, query)
  }

  // Player-scoped champion aggregate: reuse the global build payload so the
  // page renders; the numbers just read as the player's sample.
  const playerChampionMatch = path.match(/^\/truemains\/[^/]+\/champions\/(\d+)(?:\/(matchups|divergence|performance))?$/)
  if (playerChampionMatch) {
    const id = Number(playerChampionMatch[1])
    const sub = playerChampionMatch[2]
    let payload: unknown = null
    if (sub === 'matchups') payload = await mockMatchups(id)
    else if (sub === 'divergence') payload = await mockPlayerDivergence(id)
    else if (sub === 'performance') {
      payload = mockPlayerPerformance(
        id,
        typeof query.position === 'string' && query.position ? query.position : null,
        typeof query.patch === 'string' && query.patch ? query.patch : null,
      )
    }
    else payload = await mockChampionDetail(id, undefined, undefined)
    if (payload === null) throw createError({ statusCode: 404, statusMessage: 'No data (dev mock)' })
    return payload
  }

  return undefined
}
