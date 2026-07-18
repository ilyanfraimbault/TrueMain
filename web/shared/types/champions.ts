export interface ChampionSummaryResponse {
  championId: number
  games: number
  wins: number
  winRate: number
  pickRate: number
  lanePlayRate: number
  trueMainCount: number
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
 * A champion's average lead vs its lane opponent at each minute mark
 * (5/10/15/20/30), computed live from per-interval timeline snapshots. Positive
 * diffs mean the champion is ahead of the opposing lane at that interval.
 */
export interface ChampionTimelineLeadsResponse {
  championId: number
  position: string
  patch: string | null
  intervals: ChampionTimelineLeadsInterval[]
}

export interface ChampionTimelineLeadsInterval {
  intervalMinute: number
  games: number
  goldDiff: number
  csDiff: number
  killsDiff: number
  /** Not surfaced in the chart selector — level leads are too coarse to read at a glance. */
  levelDiff: number
  xpDiff: number
  damageDiff: number
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
 * Power curve and event spikes for a champion at a position (issue #571). The
 * curve is the mean opponent-relative power per minute; the events are the
 * completed build items and level milestones (6/11/16), each carrying how much
 * the curve accelerates around it — the spike.
 */
export interface ChampionPowerspikesResponse {
  championId: number
  position: string
  patch: string | null
  /** Mean power per minute across the population, ordered by minute. */
  curve: ChampionPowerCurvePoint[]
  /** Spike events, ordered by descending magnitude. */
  events: ChampionPowerspikeEvent[]
}

export interface ChampionPowerCurvePoint {
  minute: number
  /**
   * Opponent-relative power index at this minute: 0 = even with the lane
   * opponent, positive = ahead. Unitless (σ-normalized blend of gold and
   * damage lead).
   */
  power: number
  games: number
}

export interface ChampionPowerspikeEvent {
  type: 'item' | 'level'
  /** Item id for `item` events; champion level (6/11/16) for `level` events. */
  refId: number
  /** Mean minute the event occurs across games. */
  avgMinute: number
  /**
   * Mean change in the power-curve slope across a ±3 min window around the
   * event (after − before). Positive = the champion's advantage accelerates
   * after the event — the power spike.
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
