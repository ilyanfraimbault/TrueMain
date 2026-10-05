/**
 * The win-probability model (#1795, #1864, #1911): a logistic over each lane's
 * lead and what each side holds on the map. One implementation, read by the
 * desktop overlay during a game (through `item-value.ts`'s adapter over the
 * live client) and by the post-game curve (`win-probability-timeline.ts`).
 * The ingestor runs a C# port of it (`Core/Lol/WinProbability`); the fixture
 * `web/shared/fixtures/win-probability-timeline.json` holds both to the same
 * figures, so a weight changed on one side fails the other's tests.
 */

/** The lanes the lead weights are fitted for, in the draft's order. */
export const WIN_PROBABILITY_LANES = ['TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY'] as const
export type WinProbabilityLane = (typeof WIN_PROBABILITY_LANES)[number]

/**
 * The model's weights, in log-odds, fitted on TrueMain's own ranked games
 * (2026-10-04, about a million per minute mark): a logistic regression of the
 * win on each lane's creep-score, level and kill lead over the opposite lane,
 * at 5, 10, 15, 20 and 30 minutes. Only what the game's API shows for all ten
 * players goes in — it gives nobody's gold but ours, and item gold lags behind
 * the side ahead (the side behind shops each time it dies). The support's lead
 * weighs little: its kills about half a carry's, its creep score nothing (a
 * support farming takes from the carry), its gold measured at about a third of
 * a carry's per thousand.
 *
 * Per lane: per ten creep score, per level, per kill.
 */
export const WIN_PROBABILITY_MINUTES = [5, 10, 15, 20, 30] as const
export const LEAD_WEIGHTS: Record<WinProbabilityLane, { cs: number[], level: number[], kill: number[] }> = {
  TOP: { cs: [0.116, 0.074, 0.052, 0.049, 0.049], level: [0.033, 0.079, 0.123, 0.184, 0.257], kill: [0.206, 0.15, 0.109, 0.086, 0.055] },
  JUNGLE: { cs: [0.269, 0.178, 0.101, 0.064, 0.035], level: [0.045, 0.073, 0.149, 0.214, 0.279], kill: [0.269, 0.193, 0.135, 0.1, 0.06] },
  MIDDLE: { cs: [0.159, 0.115, 0.088, 0.065, 0.042], level: [0.039, 0.095, 0.163, 0.212, 0.264], kill: [0.243, 0.175, 0.112, 0.08, 0.054] },
  BOTTOM: { cs: [0.202, 0.149, 0.112, 0.095, 0.062], level: [0.023, 0.056, 0.13, 0.196, 0.266], kill: [0.229, 0.173, 0.133, 0.111, 0.079] },
  UTILITY: { cs: [-0.037, -0.039, -0.046, -0.033, 0.001], level: [0.04, 0.065, 0.168, 0.258, 0.341], kill: [0.127, 0.079, 0.033, 0.017, 0.021] },
}

/**
 * The map's weights, in log-odds — the product owner's call (2026-10-02), not
 * measured: the stored timelines keep no objectives. Three turrets ahead are
 * about +9 points from even, an inhibitor down about +12, the Baron's buff
 * about +21, the Elder's about +25.
 */
export const MAP_WEIGHTS = {
  /** Per enemy turret destroyed beyond the other side's count. */
  turret: 0.12,
  /** Per enemy inhibitor down right now. */
  inhibitor: 0.5,
  /** Per elemental drake beyond the other side's count. */
  dragon: 0.15,
  /** On top, for the side on four drakes: the soul. */
  soul: 0.6,
  /** While the side holds the Baron's buff. */
  baron: 0.9,
  /** While the side holds the Elder's buff. */
  elder: 1.1,
} as const

/** Drakes for the dragon soul. */
export const SOUL = 4
/** How long a destroyed inhibitor stays down, in seconds. */
export const INHIBITOR_RESPAWN_SECONDS = 300
/** How long the Baron's buff lasts, in seconds. */
export const BARON_BUFF_SECONDS = 180
/** How long the Elder's buff lasts, in seconds. */
export const ELDER_BUFF_SECONDS = 150

/** A weight at `clock` (game seconds): linear between two marks, the nearest one outside them. */
export function weightAt(weights: number[], clock: number): number {
  const minute = clock / 60
  const first = WIN_PROBABILITY_MINUTES[0]
  const last = WIN_PROBABILITY_MINUTES[WIN_PROBABILITY_MINUTES.length - 1]!
  if (minute <= first) return weights[0]!
  if (minute >= last) return weights[weights.length - 1]!
  const upper = WIN_PROBABILITY_MINUTES.findIndex(mark => mark >= minute)
  const from = WIN_PROBABILITY_MINUTES[upper - 1]!
  const to = WIN_PROBABILITY_MINUTES[upper]!
  return weights[upper - 1]! + (weights[upper]! - weights[upper - 1]!) * (minute - from) / (to - from)
}

/** One lane's lead, one side over the other: creep score, level, kills. */
export interface LaneLead {
  lane: WinProbabilityLane
  cs: number
  level: number
  kills: number
}

/** What one side holds on the map at a moment. */
export interface SideMap {
  /** Enemy turrets this side destroyed. */
  turrets: number
  /** Enemy inhibitors this side holds down right now. */
  inhibitorsDown: number
  /** Elemental drakes this side slew; the Elder is not one of them. */
  dragons: number
  /** The Baron's buff is up for this side. */
  baron: boolean
  /** The Elder's buff is up for this side. */
  elder: boolean
}

function mapEdge(side: SideMap): number {
  return MAP_WEIGHTS.turret * side.turrets
    + MAP_WEIGHTS.inhibitor * side.inhibitorsDown
    + MAP_WEIGHTS.dragon * side.dragons
    + (side.dragons >= SOUL ? MAP_WEIGHTS.soul : 0)
    + (side.baron ? MAP_WEIGHTS.baron : 0)
    + (side.elder ? MAP_WEIGHTS.elder : 0)
}

function laneEdge(leads: LaneLead[], clock: number): number {
  let edge = 0
  for (const lead of leads) {
    const weights = LEAD_WEIGHTS[lead.lane]
    if (!weights) continue
    edge += weightAt(weights.cs, clock) * lead.cs / 10
      + weightAt(weights.level, clock) * lead.level
      + weightAt(weights.kill, clock) * lead.kills
  }
  return edge
}

/**
 * One side's chance to win at `clock` (game seconds): a logistic of its lanes'
 * leads over the other side's (`leads`, each one ours minus theirs; none in a
 * queue without lanes) and of what each side holds on the map. Even (0.5) when
 * nothing separates them.
 */
export function winProbability(input: { leads: LaneLead[], ours: SideMap, theirs: SideMap, clock: number }): number {
  const edge = laneEdge(input.leads, input.clock) + mapEdge(input.ours) - mapEdge(input.theirs)
  return 1 / (1 + Math.exp(-edge))
}
