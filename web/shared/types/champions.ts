import type { ProfileIdentity } from './profile'

export interface ChampionSummaryResponse {
  championId: number
  games: number
  wins: number
  winRate: number
  pickRate: number
  lanePlayRate: number
  trueMainCount: number
  /**
   * Share of observed matches that banned this champion, or null on a patch older
   * than ban ingestion (#920). Null means "not observed", not "never banned" —
   * render the gap, never a 0%. Not addable to `pickRate`: the two have different
   * denominators (every observed match vs tracked mains' games at this lane).
   */
  banRate: number | null
  /** OPGG-style performance tier: 'S' | 'A' | 'B' | 'C' | 'D' (patch-relative). */
  tier: string
  position: string
  patchVersion: string
  lastUpdatedAtUtc: string
  topBuild: ChampionSummaryTopBuild | null
}

export interface ChampionSummaryTopBuild {
  firstItemId: number
  primaryKeystoneId: number
  secondaryStyleId: number
  itemPath: number[]
}

/**
 * Champion meta / tier-list for a single patch (`GET /champions/tierlist`).
 * Champions are bucketed into S/A/B/C/D tiers by a winRate + pickRate blend,
 * tiered independently per position. All metrics come from the same aggregates
 * the directory reads — none are synthesised.
 */
export interface ChampionTierListResponse {
  patchVersion: string
  /** Position the list is scoped to, or null for every position at once. */
  position: string | null
  /** Tier groups in descending strength (S first); empty tiers are omitted. */
  tiers: ChampionTierGroup[]
}

export interface ChampionTierGroup {
  /** Tier letter: 'S' | 'A' | 'B' | 'C' | 'D'. */
  tier: string
  /** Rows in this tier, strongest-first. */
  entries: ChampionTierEntry[]
}

export interface ChampionTierEntry {
  championId: number
  position: string
  games: number
  winRate: number
  /** Share of TrueMain games on this position taken by this champion. */
  pickRate: number
  /** Share of observed matches that banned this champion; null before #920's data. */
  banRate: number | null
}

export interface ChampionResponse {
  championId: number
  patch: string
  position: string
  /**
   * Elo filter this slice was computed for: `ALL`, a bare tier (e.g. `GOLD` —
   * that tier only) or a `<TIER>_PLUS` form (e.g. `GOLD_PLUS` — that tier and
   * above). `ALL` by default.
   */
  eloBracket: string
  /**
   * Games in the selected bracket as a fraction of all games on this champion at
   * the resolved patch + position. `1` for the `ALL` bracket; lower for narrow
   * high-elo bands, so the page can flag how representative the slice is.
   */
  eloCoverage: number
  /**
   * False when `totalGames` is below the trustworthy-build floor (tiny
   * high-bracket slices). The page still renders the data but flags it as
   * low-confidence.
   */
  minSampleMet: boolean
  totalGames: number
  totalWins: number
  builds: ChampionBuild[]
}

export interface ChampionTrendResponse {
  championId: number
  position: string
  points: ChampionTrendPoint[]
}

export interface ChampionTrendPoint {
  patch: string
  winRate: number
  pickRate: number
  /**
   * Share of the patch's observed matches that banned this champion, all elo bands
   * (the trend endpoint takes no rank filter). Null on any patch older than ban
   * ingestion (#920) — early on, most of the series is null.
   */
  banRate: number | null
  games: number
}

/**
 * What changed for a champion between two patches (issue #534): the win-rate
 * swing plus whether the dominant first item, keystone and skill order moved,
 * at a single position. Either side is null when the champion has no data on
 * that patch; `delta` is null unless both sides are present.
 */
export interface ChampionPatchDiffResponse {
  championId: number
  position: string
  /** Distinct patches with data for this champion/position; the section hides below 2. */
  availablePatchCount: number
  from: ChampionPatchDiffSide | null
  to: ChampionPatchDiffSide | null
  delta: ChampionPatchDiffDelta | null
}

export interface ChampionPatchDiffSide {
  patch: string
  games: number
  wins: number
  winRate: number
  /** Top build's completed core item order on the patch; null when none qualifies. */
  itemPath: BuildItemPath | null
  /** Top build's full rune page on the patch; null when unavailable. */
  runePage: BuildRunePage | null
  /** Top build's dominant skill-order sequence; null when unavailable. */
  skillOrder: BuildSkillOrder | null
}

export interface ChampionPatchDiffDelta {
  /** Win-rate change, to.winRate - from.winRate (signed fraction). */
  winRateChange: number
  firstItemChanged: boolean
  keystoneChanged: boolean
  skillOrderChanged: boolean
}

/**
 * How a champion's win rate changes with game length, at a position. Win rate is
 * bucketed by game duration; `scalingIndex` is the win-rate gap between the
 * longest and shortest qualifying bucket (positive = scales into the late game).
 */
export interface ChampionScalingResponse {
  championId: number
  position: string
  patch: string | null
  buckets: ChampionScalingBucket[]
  scalingIndex: number | null
}

export interface ChampionScalingBucket {
  /** Duration bucket index, 0 (shortest) to 4 (longest). */
  bucket: number
  label: string
  games: number
  winRate: number
}

/**
 * Event spikes for a champion at a position, scoped to one core build (issue
 * #571, scoped per build in #890): the items that build completes and the level
 * milestones (6/11/16), each carrying how much the champion's power accelerates
 * around it. The mean power curve is no longer returned — it is only the
 * baseline the spikes are measured against, server-side.
 */
export interface ChampionPowerspikesResponse {
  championId: number
  position: string
  patch: string | null
  /** Spike events, ordered by descending magnitude. */
  events: ChampionPowerspikeEvent[]
}

export interface ChampionPowerspikeEvent {
  type: 'item' | 'level'
  /** Item id for `item` events; champion level (6/11/16) for `level` events. */
  refId: number
  /** Mean minute the event occurs across games. */
  avgMinute: number
  /**
   * Mean change in the power-curve slope across a ±3 min window around the
   * event (after − before), in excess of the baseline curvature the mean curve
   * shows at that minute. Positive = the champion's advantage accelerates after
   * the event beyond the norm — the power spike.
   */
  spikeMagnitude: number
  games: number
}

/**
 * How much a champion roams at a position: the average number of out-of-lane
 * kill participations (kills + assists) per game at the 5/10/15-minute marks
 * (cumulative). A roam is a participation in a different lane, the enemy jungle,
 * or the enemy base. The `roamKp*` values are null below the sample floor and
 * for JUNGLE (which has no own lane).
 */
export interface ChampionRoamResponse {
  championId: number
  position: string
  patch: string | null
  games: number
  roamKp5: number | null
  roamKp10: number | null
  roamKp15: number | null
}

/** One lane-matchup row: the champion's record against a single opponent. */
export interface ChampionMatchupEntry {
  opponentChampionId: number
  games: number
  wins: number
  winRate: number
}

/**
 * All of a champion's lane matchups at a position, computed live from match
 * participants. The client slices a best/worst leaderboard out of it and
 * filters it for the opponent search.
 */
export interface ChampionMatchups {
  championId: number
  position: string
  patch: string | null
  matchups: ChampionMatchupEntry[]
}

/**
 * One duo partner row. `synergy` is the value the list is ranked by — how far
 * `winRate` lands above (positive) or below what the two champions' own win
 * rates already predicted. A high `winRate` with a `synergy` near zero means
 * "this champion wins a lot", not "this pairing works".
 */
export interface ChampionSynergyEntry {
  partnerChampionId: number
  partnerPosition: string
  games: number
  wins: number
  winRate: number
  /** Sample behind `partnerBaselineWinRate`; always ≥ `games`. */
  partnerBaselineGames: number
  /** The partner's win rate as somebody's teammate, across all their pairings. */
  partnerBaselineWinRate: number
  expectedWinRate: number
  /** `winRate - expectedWinRate`, on the same 0–1 scale as a win rate. */
  synergy: number
}

/** `GET /api/champions/{id}/synergies` — the duo half of the synergies panel. */
export interface ChampionSynergies {
  championId: number
  position: string
  patch: string | null
  /** The partner lane the request narrowed to, or null for every lane. */
  partnerPosition: string | null
  /** Minimum shared games a pairing needed to appear — echoed to explain an empty list. */
  minGames: number
  /** Sample behind `championWinRate`. Zero means the champion has no games in scope. */
  championGames: number
  championWinRate: number
  /** The tracked cohort's overall win rate — the reference point partners are judged against. */
  cohortWinRate: number
  partners: ChampionSynergyEntry[]
}

/** One third-pick row for a chosen duo. */
export interface ChampionTrioSynergyEntry {
  championId: number
  position: string
  games: number
  wins: number
  winRate: number
  baselineGames: number
  baselineWinRate: number
  expectedWinRate: number
  synergy: number
}

/**
 * `GET /api/champions/{id}/synergies/trios` — computed live from the games the
 * chosen duo actually shared, so `pairGames` is the ceiling every completion's
 * sample is drawn from and an empty `completions` list is the normal answer for
 * a rarely-played duo.
 */
export interface ChampionTrioSynergies {
  championId: number
  position: string
  partnerChampionId: number
  partnerPosition: string
  patch: string | null
  minGames: number
  pairGames: number
  pairWins: number
  pairWinRate: number
  completions: ChampionTrioSynergyEntry[]
}

/**
 * Why an account-vs-mains comparison did — or did not — produce two comparable
 * columns. Mirrors `ChampionComparisonStatus` in
 * backend/Api/ReadModels/Champions/ChampionMainsComparisonResponse.cs.
 */
export type ChampionComparisonStatus
  = | 'OK'
    | 'UNKNOWN_ACCOUNT'
    | 'UNKNOWN_TARGET'
    | 'INSUFFICIENT_SAMPLE'

/** One column of the comparison. Counting stats are per-game averages. */
export interface ChampionComparisonSide {
  /** Null for the aggregate of the champion's mains — it has no single owner. */
  identity: ProfileIdentity | null
  /** Distinct accounts behind the column: 1 for a player, the pool size for the aggregate. */
  players: number
  games: number
  wins: number
  /** `wins / games`; 0 when the side has no games. */
  winRate: number
  /** Kills per game. */
  kills: number
  /** Deaths per game. */
  deaths: number
  /** Assists per game. */
  assists: number
  /** `(kills + assists) / deaths`, falling back to `kills + assists` on a deathless sample. */
  kda: number
  /** Minions + monsters per minute, over the summed game durations. */
  csPerMin: number
  /** Gold per minute, same denominator as `csPerMin`. */
  goldPerMin: number
  goldPerGame: number
  /** Whether `games` reached the response's `minGames` floor. */
  sampleMet: boolean
}

/**
 * Head-to-head between a Riot account and a champion's mains (#528). The
 * lookup is database-only — an account we have never ingested comes back as
 * `UNKNOWN_ACCOUNT` with no columns, never as an error.
 */
export interface ChampionMainsComparison {
  championId: number
  /** Resolved patch, or null when the slice spans every stored patch. */
  patch: string | null
  /** Lane both sides were narrowed to, or null for every lane. */
  position: string | null
  /** Games each side needs before the comparison counts as meaningful. */
  minGames: number
  status: ChampionComparisonStatus
  /** Null only when the account is unknown to us. */
  player: ChampionComparisonSide | null
  /** Null when the account — or a targeted main — is unknown to us. */
  mains: ChampionComparisonSide | null
}

export interface ChampionBuild {
  firstItemId: number
  primaryKeystoneId: number
  games: number
  pickRate: number
  winRate: number
  core: BuildCore
  variations: BuildVariations
  buildTree: BuildTreeNode[]
  runePages: BuildRunePage[]
}

export interface BuildCore {
  itemPath: BuildItemPath | null
  boots: BuildItemSet | null
  starterItems: BuildItemSet | null
  summonerSpells: BuildSummonerSpells | null
  skillOrder: BuildSkillOrder | null
  runePage: BuildRunePage | null
}

export interface BuildVariations {
  boots: BuildItemSet[]
  starterItems: BuildItemSet[]
  summonerSpells: BuildSummonerSpells[]
  skillOrder: BuildSkillOrder[]
}

export interface BuildTreeNode {
  itemId: number
  games: number
  wins: number
  pickRate: number
  children: BuildTreeNode[]
}

export interface BuildItemPath {
  itemIds: number[]
  games: number
  pickRate: number
  winRate: number
}

export interface BuildItemSet {
  itemIds: number[]
  games: number
  pickRate: number
  winRate: number
}

export interface BuildSummonerSpells {
  spell1Id: number
  spell2Id: number
  games: number
  pickRate: number
  winRate: number
}

export interface BuildSkillOrder {
  sequence: string[]
  games: number
  pickRate: number
  winRate: number
}

export interface BuildRunePage {
  primaryStyleId: number
  primaryKeystoneId: number
  primaryPerk1Id: number
  primaryPerk2Id: number
  primaryPerk3Id: number
  secondaryStyleId: number
  secondaryPerk1Id: number
  secondaryPerk2Id: number
  statOffense: number
  statFlex: number
  statDefense: number
  games: number
  pickRate: number
  winRate: number
}
