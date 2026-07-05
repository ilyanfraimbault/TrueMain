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
  ChampionBuild,
  ChampionItemTimingsResponse,
  ChampionMatchups,
  ChampionResponse,
  ChampionRoamResponse,
  ChampionScalingResponse,
  ChampionSummaryResponse,
  ChampionTimelineLeadsResponse,
  ChampionTrendResponse,
  BuildRunePage,
} from '~~/shared/types/champions'
import type {
  LeaderboardResponse,
  LeaderboardRowResponse,
  RegionSlug,
} from '~~/shared/types/leaderboard'
import type { MatchSummariesResponse, MatchSummaryResponse } from '~~/shared/types/matches'
import type { ProfileResponse } from '~~/shared/types/profile'
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

async function mockChampionSummaries(): Promise<ChampionSummaryResponse[]> {
  const patch = await latestShortPatch()
  // Tier is patch-relative: rank rows by win rate and bucket by percentile,
  // mirroring ChampionTierCalculator on the backend.
  const byWr = [...CHAMPION_SEEDS].sort((a, b) => b.wr - a.wr)
  const tierByRow = new Map(byWr.map((s, i) => [s, tierFor(i, byWr.length)]))

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
      tier: tierByRow.get(s) ?? 'B',
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

async function mockChampionDetail(id: number, position: string | undefined): Promise<ChampionResponse | null> {
  const s = seedsById.get(id)
  if (!s) return null
  // Mirror the backend: a filtered slice for a lane the champion doesn't play
  // is a 404, and the client falls back to the default (unfiltered) slice.
  if (position && position !== s.position) return null
  const patch = await latestShortPatch()
  const rng = mulberry32(s.id * 31 + s.position.length)
  const totalGames = Math.max(120, Math.round(s.pr * POOL_GAMES * (0.9 + rng() * 0.2)))
  return {
    championId: s.id,
    patch,
    position: s.position,
    totalGames,
    totalWins: Math.round(totalGames * s.wr),
    builds: [makeBuild(s, 0, totalGames), makeBuild(s, 1, totalGames)],
  }
}

// ─── Champion insight endpoints ──────────────────────────────────────────────

async function mockTrend(id: number): Promise<ChampionTrendResponse | null> {
  const s = seedsById.get(id)
  if (!s) return null
  const latest = await latestShortPatch()
  const rng = mulberry32(s.id * 131)
  return {
    championId: s.id,
    position: s.position,
    points: trendPatches(latest, 6).map(patch => ({
      patch,
      winRate: round3(s.wr + (rng() - 0.5) * 0.03),
      pickRate: round3(Math.max(0.004, s.pr + (rng() - 0.5) * 0.02)),
      games: Math.round(s.pr * POOL_GAMES * (0.8 + rng() * 0.4)),
    })),
  }
}

async function mockTimelineLeads(id: number): Promise<ChampionTimelineLeadsResponse | null> {
  const s = seedsById.get(id)
  if (!s) return null
  const rng = mulberry32(s.id * 211)
  // Above-50% champions trend ahead, below-50% behind; leads widen with time.
  const bias = (s.wr - 0.5) * 20
  return {
    championId: s.id,
    position: s.position,
    patch: await latestShortPatch(),
    intervals: [5, 10, 15, 20, 30].map((minute, i) => {
      const drift = (i + 1) * (bias + (rng() - 0.4) * 4)
      return {
        intervalMinute: minute,
        games: Math.round(s.pr * POOL_GAMES * (1 - i * 0.13)),
        goldDiff: Math.round(drift * 55),
        csDiff: round3(drift * 0.55),
        killsDiff: round3(drift * 0.045),
        levelDiff: round3(drift * 0.02),
        xpDiff: Math.round(drift * 38),
        damageDiff: Math.round(drift * 140),
      }
    }),
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

async function mockItemTimings(id: number): Promise<ChampionItemTimingsResponse | null> {
  const s = seedsById.get(id)
  if (!s) return null
  const rng = mulberry32(s.id * 401)
  const archetype = ARCHETYPES[s.archetype]
  return {
    championId: s.id,
    position: s.position,
    patch: await latestShortPatch(),
    items: archetype.items.slice(0, 6).map((itemId, i) => ({
      itemId,
      games: Math.round(s.pr * POOL_GAMES * Math.max(0.08, 0.7 - i * 0.11)),
      avgSeconds: Math.round(520 + i * 290 + rng() * 90),
    })),
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
    matchups: opponents.map((o) => {
      const games = Math.round(40 + rng() * 360)
      const winRate = round3(Math.min(0.6, Math.max(0.4, 0.5 + (s.wr - o.wr) * 1.6 + (rng() - 0.5) * 0.07)))
      return { opponentChampionId: o.id, games, wins: Math.round(games * winRate), winRate }
    }),
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
        topChampions: mains.map((m, idx) => {
          const playRate = [0.44, 0.21, 0.12][idx]! + rng() * 0.08
          return {
            championId: m.id,
            games: Math.round(games * playRate),
            playRate: round3(playRate),
            primaryKeystoneId: m.keystone,
            secondaryStyleId: m.secondaryStyle,
            firstItemId: ARCHETYPES[m.archetype].items[0]!,
          }
        }),
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

  let rows = players()
  if (region) rows = rows.filter(p => p.row.region === region)
  if (position) rows = rows.filter(p => p.position === position)
  if (championId) rows = rows.filter(p => p.row.topChampions.some(c => c.championId === championId))

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
      isOtp: c.playRate > 0.6,
    })),
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
        win,
        lpDelta: win ? 14 + Math.floor(rng() * 12) : -(12 + Math.floor(rng() * 12)),
        isMvp: win && rng() > 0.72,
        isAce: !win && rng() > 0.85,
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
export async function resolveDevApiMock(
  path: string,
  query: Record<string, unknown>,
): Promise<unknown | undefined> {
  if (path === '/champions') return mockChampionSummaries()

  const championMatch = path.match(/^\/champions\/(\d+)(?:\/([a-z-]+))?$/)
  if (championMatch) {
    const id = Number(championMatch[1])
    const sub = championMatch[2]
    const position = typeof query.position === 'string' && query.position ? query.position : undefined
    const payload = await (
      sub === undefined ? mockChampionDetail(id, position)
      : sub === 'trend' ? mockTrend(id)
      : sub === 'timeline-leads' ? mockTimelineLeads(id)
      : sub === 'scaling' ? mockScaling(id)
      : sub === 'item-timings' ? mockItemTimings(id)
      : sub === 'roam' ? mockRoam(id)
      : sub === 'matchups' ? mockMatchups(id)
      : Promise.resolve(undefined))
    if (payload === undefined) return undefined
    if (payload === null) {
      throw createError({ statusCode: 404, statusMessage: 'No data for this champion (dev mock)' })
    }
    return payload
  }

  if (path === '/truemains') return mockLeaderboard(query)
  if (path === '/truemains/search') return mockSearch(typeof query.q === 'string' ? query.q : '')

  const playerMatch = path.match(/^\/truemains\/([^/]+)\/(profile|rank-history|matches)$/)
  if (playerMatch) {
    const name = safeDecodeURIComponent(playerMatch[1]!)
    const player = name === undefined ? undefined : findPlayer(name)
    if (!player) throw createError({ statusCode: 404, statusMessage: 'Unknown truemain (dev mock)' })
    if (playerMatch[2] === 'profile') return mockProfile(player)
    if (playerMatch[2] === 'rank-history') return mockRankHistory(player)
    return mockMatches(player, query)
  }

  // Player-scoped champion aggregate: reuse the global build payload so the
  // page renders; the numbers just read as the player's sample.
  const playerChampionMatch = path.match(/^\/truemains\/[^/]+\/champions\/(\d+)(?:\/matchups)?$/)
  if (playerChampionMatch) {
    const id = Number(playerChampionMatch[1])
    const payload = path.endsWith('/matchups') ? await mockMatchups(id) : await mockChampionDetail(id, undefined)
    if (payload === null) throw createError({ statusCode: 404, statusMessage: 'No data (dev mock)' })
    return payload
  }

  return undefined
}
