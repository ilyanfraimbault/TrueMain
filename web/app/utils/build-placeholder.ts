import type {
  BuildRunePage,
  BuildTreeNode,
  ChampionBuild,
} from '~~/shared/types/champions'
import type {
  ChampionStaticData,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'

/**
 * Scaffolding for the build sections' loading state.
 *
 * The champion page has two loading phases it cannot merge: the build aggregate
 * and the static bundles are separate fetches, and the ~95 DDragon icons only
 * start downloading once the ids they resolve are on screen. So the page shows
 * *a* placeholder while the API answers, then the real panels with every icon
 * still pulsing while the images land. Those two used to be different pictures
 * — a hand-drawn grid of grey blocks, then the actual layout — and the swap
 * between them read as the page reloading itself.
 *
 * Rather than keep re-drawing the panels by hand in a skeleton component (which
 * drifts from the real layout the moment a section moves), the skeleton renders
 * the *real* components with these placeholder aggregates and `pending` set.
 * Every icon then resolves to nothing and falls back to the same pulsing box
 * `SkeletonImage` shows while a real icon loads, and every number is masked —
 * so phase one is pixel-for-pixel the layout of phase two, and the only visible
 * transition left is the icons filling in.
 *
 * Sizes are chosen to match the median real build (3 tabs, a 6-item path, 3
 * boots / 3 starter alternatives, a 4-deep tree, 3 rune pages), so the space
 * reserved is close to what lands.
 *
 * Ids are arbitrary and deliberately unresolvable: the skeleton passes empty
 * item / summoner / perk maps, so every lookup misses and every slot renders
 * its loading box. Only the rune ids are internally consistent, so that the
 * selected-perk ring lands on one perk per row exactly as it does once loaded.
 */

/**
 * The empty static maps the skeleton hands to every panel — every lookup misses
 * and every icon slot renders its loading box. Shared constants rather than `{}`
 * literals in the templates so the placeholder identity is stable across renders.
 */
export const PLACEHOLDER_ITEMS_MAP: Record<number, StaticItemData> = {}
export const PLACEHOLDER_SUMMONERS_MAP: Record<number, StaticSummonerSpellData> = {}

const PRIMARY_STYLE_ID = 1
const SECONDARY_STYLE_ID = 2

/**
 * A rune tree with the real shape — 4 keystones, 3 sub-rows of 3, a secondary
 * tree and 3 shard rows — and no perk metadata, so every slot is a pulsing
 * circle in the exact grid the loaded tree occupies.
 */
export const PLACEHOLDER_RUNE_TREE: RuneTreeResponse = {
  styles: [
    {
      styleId: PRIMARY_STYLE_ID,
      name: '',
      iconUrl: '',
      keystones: [11, 12, 13, 14],
      subRows: [[15, 16, 17], [18, 19, 20], [21, 22, 23]],
    },
    {
      styleId: SECONDARY_STYLE_ID,
      name: '',
      iconUrl: '',
      keystones: [51, 52, 53],
      subRows: [[31, 32, 33], [34, 35, 36], [37, 38, 39]],
    },
  ],
  perks: {},
  perkStyles: {},
  shardSlots: [[41, 42, 43], [44, 45, 46], [47, 48, 49]],
}

function runePage(keystoneId: number, primaryPerks: [number, number, number]): BuildRunePage {
  return {
    primaryStyleId: PRIMARY_STYLE_ID,
    primaryKeystoneId: keystoneId,
    primaryPerk1Id: primaryPerks[0],
    primaryPerk2Id: primaryPerks[1],
    primaryPerk3Id: primaryPerks[2],
    secondaryStyleId: SECONDARY_STYLE_ID,
    secondaryPerk1Id: 31,
    secondaryPerk2Id: 34,
    statOffense: 41,
    statFlex: 44,
    statDefense: 47,
    games: 100,
    pickRate: 0.5,
    winRate: 0.5,
  }
}

/** No name, no icon and no spells — the skill-order icons fall back to their loading box. */
export const PLACEHOLDER_CHAMPION_STATIC: ChampionStaticData = {
  championName: null,
  championIconUrl: null,
  championSpells: {},
  partype: '',
}

function treeNode(itemId: number, children: BuildTreeNode[] = []): BuildTreeNode {
  return { itemId, games: 100, wins: 50, pickRate: 0.5, children }
}

// Four levels below the root — the depth a 6-item core path usually draws.
const PLACEHOLDER_BUILD_TREE: BuildTreeNode[] = [
  treeNode(211, [
    treeNode(221, [treeNode(231, [treeNode(241)])]),
    treeNode(222),
  ]),
  treeNode(212),
]

function placeholderBuild(offset: number): ChampionBuild {
  const rates = { games: 100, pickRate: 0.5, winRate: 0.5 }
  return {
    firstItemId: 201 + offset,
    primaryKeystoneId: 11 + offset,
    ...rates,
    core: {
      itemPath: { itemIds: [201, 202, 203, 204, 205, 206], ...rates },
      boots: { itemIds: [207], ...rates },
      starterItems: { itemIds: [208, 209, 210], ...rates },
      summonerSpells: { spell1Id: 301, spell2Id: 302, ...rates },
      skillOrder: { sequence: ['Q', 'E', 'W'], ...rates },
      runePage: runePage(11 + offset, [15, 18, 21]),
    },
    variations: {
      // Row counts mirror what the panels typically show: one dominant summoner
      // pair and skill order, a handful of boots and starter alternatives.
      summonerSpells: [{ spell1Id: 301, spell2Id: 302, ...rates }],
      skillOrder: [{ sequence: ['Q', 'E', 'W'], ...rates }],
      boots: [207, 213, 214].map(itemId => ({ itemIds: [itemId], ...rates })),
      starterItems: [208, 215, 216].map(itemId => ({ itemIds: [itemId, 209], ...rates })),
    },
    buildTree: PLACEHOLDER_BUILD_TREE,
    runePages: [
      runePage(11, [15, 18, 21]),
      runePage(12, [16, 19, 22]),
      runePage(13, [17, 20, 23]),
    ],
  }
}

/** Three tabs — the number the champion page shows for most champions. */
export const PLACEHOLDER_BUILDS: ChampionBuild[] = [0, 1, 2].map(placeholderBuild)

/**
 * Scope handed to the placeholder panel for the sole purpose of putting the
 * power-spikes section in the layout it reserves: `ChampionBuildPanel` gates
 * that section on having a (champion, position) scope. Nothing is ever
 * requested with it — `pending` zeroes the build key the spikes endpoint
 * requires, which holds the fetch.
 */
export const PLACEHOLDER_SPIKES_SCOPE = { championId: 1, position: 'MIDDLE' } as const
