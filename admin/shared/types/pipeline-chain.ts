/**
 * The ingestor pipeline chain as the admin draws it: the ordered steps, the two lanes
 * they partition into, and the label + description of every process. Split out of
 * `ops.ts` (#1449) — that file is the API contract of `/api/ops/*`, this one is the
 * hand-maintained mirror of `backend/Ingestor/Options/JobModeSequence.cs`.
 */
/**
 * The canonical ingestor pipeline chain, in execution order — one full pass runs
 * these processes in sequence. Drives the chain view: the ordered links and the
 * per-iteration outcome lookup.
 *
 * The source of truth is `backend/Ingestor/Options/JobModeSequence.cs`; this is a
 * hand-maintained copy of it, so **a step added there must be added here too**.
 * Drift is quiet rather than broken: `buildChain` appends any process it sees but
 * does not know about, so a missing step still renders — at the end of the chain,
 * under its raw name, instead of in its real position. That is exactly how
 * `RunePageDeduplication` came to be drawn after `StorageSnapshot`, several steps
 * away from where it actually runs.
 */
export const PIPELINE_CHAIN: readonly string[] = [
  'LadderSync',
  'Discovery',
  'ManualSeed',
  'Harvest',
  'Scoring',
  'MainActivity',
  'MatchIngestion',
  'MatchTeamPositionCorrection',
  'MainAnalysis',
  'MatchParticipantEloBracketEnrichment',
  'RunePageDeduplication',
  'ChampionPatternAggregation',
  'ChampionProfileAggregation',
  'ChampionItemContextAggregation',
  'ChampionMatchupLeadAggregation',
  'ChampionSynergyAggregation',
  'ChampionBanAggregation',
  'ChampionPowerspikeAggregation',
  'AccountRefresh',
  'MatchDataRetention',
  'CandidateStockSnapshot',
  'StorageSnapshot',
]

/**
 * One lane of the pipeline: the steps a single ingestor instance runs, in order.
 *
 * Since #1362 the ingestor is not one chain but two composite `JobMode`s that
 * partition it — `FetchLane` is bounded by the Riot limiter, `AggregateLane` by
 * Postgres — deployed as two containers on their own cadences. The chain view
 * renders one branch per lane, so a lane's iteration no longer paints the other
 * lane's steps as phantom "Not run" chips.
 */
export interface PipelineLane {
  id: PipelineLaneId
  label: string
  /** What paces the lane — the one thing that explains its cadence. */
  description: string
  /** Its steps, in the relative order they hold in {@link PIPELINE_CHAIN}. */
  steps: readonly string[]
}

export type PipelineLaneId = 'fetch' | 'aggregate'

/**
 * The two lanes, mirroring `JobModeSequence.FetchLanePipeline` /
 * `AggregateLanePipeline` — the same hand-maintained copy {@link PIPELINE_CHAIN}
 * is, and pinned the same way: `pipeline-lanes.test.ts` asserts they are a true
 * partition of the chain, in its order. A step in neither would stop being drawn
 * altogether (worse than the old misplacement), a step in both would be drawn
 * twice.
 */
export const PIPELINE_LANES: readonly PipelineLane[] = [
  {
    id: 'fetch',
    label: 'Fetch lane',
    description:
      'Everything that talks to Riot, paced by the per-region limiter: it reads the ladders, picks who to visit and downloads their matches.',
    steps: [
      'LadderSync',
      'Discovery',
      'ManualSeed',
      'Harvest',
      'Scoring',
      'MainActivity',
      'MatchIngestion',
      'AccountRefresh',
    ],
  },
  {
    id: 'aggregate',
    label: 'Aggregate lane',
    description:
      'Everything that only reads and writes Postgres: it folds the matches already downloaded into the stats the site serves, then prunes what is no longer needed.',
    steps: [
      'MatchTeamPositionCorrection',
      'MainAnalysis',
      'MatchParticipantEloBracketEnrichment',
      'RunePageDeduplication',
      'ChampionPatternAggregation',
      'ChampionProfileAggregation',
      'ChampionItemContextAggregation',
      'ChampionMatchupLeadAggregation',
      'ChampionSynergyAggregation',
      'ChampionBanAggregation',
      'ChampionPowerspikeAggregation',
      'MatchDataRetention',
      'CandidateStockSnapshot',
      'StorageSnapshot',
    ],
  },
]

/**
 * Display metadata for one pipeline process: the name shown on a chain chip, and
 * the sentence explaining what the step does.
 */
export interface ProcessMeta {
  label: string
  description: string
}

/**
 * Label and one-line explanation per process, keyed by the `processName` the
 * ingestor records. Lives next to {@link PIPELINE_CHAIN} on purpose: the order of
 * the chain and the naming of the chain are the same maintenance act, and keeping
 * them in two files is how the label map came to be missing half its entries.
 *
 * Descriptions state what the process actually does — the pipeline table in
 * `.claude/docs/features.md` is the reference. A step whose behaviour changes
 * updates its sentence here in the same PR.
 */
export const PROCESS_META: Record<string, ProcessMeta> = {
  LadderSync: {
    label: 'Ladder Sync',
    description:
      'Refreshes the rank of accounts we already track by reading the ladders themselves: the three apex tiers whole every run, then the tiers below Master page by page under a request budget.',
  },
  Discovery: {
    label: 'Discovery',
    description:
      'Walks the Master/Grandmaster/Challenger ladders to find accounts we do not track yet, and derives candidates from their champion mastery.',
  },
  ManualSeed: {
    label: 'Manual Seed',
    description:
      'Drains the seed requests added by hand from the admin, queueing them directly instead of making them compete for a top-N slot.',
  },
  Harvest: {
    label: 'Harvest',
    description:
      'Turns players already seen in ingested matches into candidates, at no Riot API cost.',
  },
  Scoring: {
    label: 'Scoring',
    description:
      'Ranks candidates on recency, rank, mastery and champion scarcity, and promotes the per-platform top N to the ingestion queue.',
  },
  MainActivity: {
    label: 'Main Activity',
    description:
      'Retires mains who stopped playing and reactivates those who came back, judged on champion-mastery last-play time. Flags rows, never deletes them.',
  },
  MatchIngestion: {
    label: 'Match Ingestion',
    description:
      'Claims queued accounts and fetches their matches and timelines — the raw rows every aggregation below reads.',
  },
  MatchTeamPositionCorrection: {
    label: 'Lane Position Fix',
    description:
      'Fills in the lane of a participant Riot left blank, for the unambiguous case where only one position in the team is missing.',
  },
  MainAnalysis: {
    label: 'Main Analysis',
    description:
      'Decides who is a true main of which champion, from play rate against adaptive thresholds, and demotes those who no longer qualify.',
  },
  MatchParticipantEloBracketEnrichment: {
    label: 'Elo Bracketing',
    description:
      'Stamps each game with the rank its players were at when they played it, so every stat below can be filtered by elo.',
  },
  RunePageDeduplication: {
    label: 'Rune Page Dedup',
    description:
      'Collapses rune pages that differ only by the order they were clicked in, so one real page is not counted as several.',
  },
  ChampionPatternAggregation: {
    label: 'Builds & Runes',
    description:
      'Rebuilds the per-champion aggregates behind the build, rune, skill-order and summoner-spell panels.',
  },
  ChampionProfileAggregation: {
    label: 'Champion Profiles',
    description:
      'Measures what each champion does in its games — damage split, healing, crowd control, damage taken, lane leads, item archetypes — the profiles that later qualify a draft as AP-heavy, tanky or sustain-heavy.',
  },
  ChampionItemContextAggregation: {
    label: 'Item Context',
    description:
      'Counts how often each item is built in games against each kind of draft, then works out which situations actually move that choice — the "why this item" behind the build tree.',
  },
  ChampionMatchupLeadAggregation: {
    label: 'Matchups',
    description:
      'Folds each match into the champion-versus-champion matchup stats — who won the game, and who won lane at 15 minutes — counting only games where the champion side is one of its mains.',
  },
  ChampionSynergyAggregation: {
    label: 'Synergies',
    description:
      'Folds each match into the same-team champion pair stats behind the synergy panel.',
  },
  ChampionBanAggregation: {
    label: 'Bans',
    description:
      'Counts champion-select bans and the match totals they are divided by, to produce ban rates.',
  },
  ChampionPowerspikeAggregation: {
    label: 'Power Spikes',
    description:
      'Measures when a champion pulls ahead over the course of a game, while the dense per-minute timeline data still exists.',
  },
  AccountRefresh: {
    label: 'Account Refresh',
    description:
      'Refreshes Riot ID and rank one account at a time, and recovers or invalidates accounts whose PUUID stopped resolving. The fallback behind Ladder Sync.',
  },
  MatchDataRetention: {
    label: 'Retention',
    description:
      'Deletes what the site no longer serves — stale candidates, out-of-window matches, intermediate timeline snapshots — to keep the database from growing without bound.',
  },
  CandidateStockSnapshot: {
    label: 'Candidate Stock',
    description:
      'Counts how many candidates sit in each stage of the funnel and records the reading, so the backlog can be charted over time. Runs after retention, so the level it stores is the one the pipeline actually sits at.',
  },
  StorageSnapshot: {
    label: 'Storage Snapshot',
    description:
      'Records the day\'s database size per table, feeding the disk-usage forecast. Runs last so it measures the size after retention, not before.',
  },
}
