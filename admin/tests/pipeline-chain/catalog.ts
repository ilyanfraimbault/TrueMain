import type { PipelineCatalogReadModel } from '#site-shared/contract/api'

/**
 * A pipeline catalog as `GET /ops/pipeline` answers it, for the chain helpers' and the
 * Processes components' tests: the steps, labels and lanes of the real `PipelineCatalog`
 * when this was written.
 */
const STEPS: readonly (readonly [name: string, lane: 'fetch' | 'aggregate', label: string, description: string])[] = [
  ['LadderSync', 'fetch', 'Ladder Sync', 'Refreshes the rank of accounts we already track by reading the ladders themselves: the three apex tiers whole every run, then the tiers below Master page by page under a request budget.'],
  ['Discovery', 'fetch', 'Discovery', 'Walks the Master/Grandmaster/Challenger ladders to find accounts we do not track yet, and derives candidates from their champion mastery.'],
  ['ManualSeed', 'fetch', 'Manual Seed', 'Drains the seed requests added by hand from the admin, queueing them directly instead of making them compete for a top-N slot.'],
  ['Harvest', 'fetch', 'Harvest', 'Turns players already seen in ingested matches into candidates, at no Riot API cost.'],
  ['Scoring', 'fetch', 'Scoring', 'Ranks candidates on recency, rank, mastery and champion scarcity, and promotes the per-platform top N to the ingestion queue.'],
  ['MainActivity', 'fetch', 'Main Activity', 'Retires mains who stopped playing and reactivates those who came back, judged on champion-mastery last-play time. Flags rows, never deletes them.'],
  ['MatchIngestion', 'fetch', 'Match Ingestion', 'Claims queued accounts and fetches their matches and timelines — the raw rows every aggregation below reads.'],
  ['MainAnalysis', 'aggregate', 'Main Analysis', 'Decides who is a true main of which champion, from play rate against adaptive thresholds, and demotes those who no longer qualify.'],
  ['MatchParticipantEloBracketEnrichment', 'aggregate', 'Elo Bracketing', 'Stamps each game with the rank its players were at when they played it, so every stat below can be filtered by elo.'],
  ['ChampionPatternAggregation', 'aggregate', 'Builds & Runes', 'Rebuilds the per-champion aggregates behind the build, rune, skill-order and summoner-spell panels.'],
  ['ChampionProfileAggregation', 'aggregate', 'Champion Profiles', 'Measures what each champion does in its games — damage split, healing, crowd control, damage taken, lane leads, item archetypes — the profiles that later qualify a draft as AP-heavy, tanky or sustain-heavy.'],
  ['ChampionItemContextAggregation', 'aggregate', 'Item Context', 'Counts how often each item is built in games against each kind of draft, then works out which situations actually move that choice — the "why this item" behind the build tree.'],
  ['ChampionMatchupLeadAggregation', 'aggregate', 'Matchups', 'Folds each match into the champion-versus-champion matchup stats — who won the game, and who won lane at 15 minutes — counting only games where the champion side is one of its mains.'],
  ['ChampionSynergyAggregation', 'aggregate', 'Synergies', 'Folds each match into the same-team champion pair stats behind the synergy panel.'],
  ['ChampionOpponentAggregation', 'aggregate', 'Opponents', 'Folds each match into the champion-versus-enemy pair stats behind the draft\'s enemy-team component.'],
  ['ChampionScalingAggregation', 'aggregate', 'Scaling', 'Folds each match into the champion win-rate-by-game-length stats behind the scaling chart, counting only games where the champion side is one of its mains.'],
  ['ChampionBanAggregation', 'aggregate', 'Bans', 'Counts champion-select bans and the match totals they are divided by, to produce ban rates.'],
  ['AccountRefresh', 'fetch', 'Account Refresh', 'Refreshes Riot ID and rank one account at a time, and recovers or invalidates accounts whose PUUID stopped resolving. The fallback behind Ladder Sync.'],
  ['PaceSampling', 'fetch', 'Pace Sampling', 'Reads a few games of the tiers TrueMain does not ingest (Iron to Platinum) into the desktop overlay\'s pace benchmark, under its own daily call cap, and keeps none of them — only the counters and the ids already read.'],
  ['MatchDataRetention', 'aggregate', 'Retention', 'Deletes what the site no longer serves — stale candidates, out-of-window matches, intermediate timeline snapshots — to keep the database from growing without bound.'],
  ['ChampionGameCountRefresh', 'aggregate', 'Champion Game Counts', 'Recounts the stored games per champion, region, patch and lane from the raw matches, so the Champions page and the Overview read a small table instead of scanning every participant. At most every half hour, after retention, so a deleted patch stops counting.'],
  ['CandidateStockSnapshot', 'aggregate', 'Candidate Stock', 'Counts how many candidates sit in each stage of the funnel and records the reading, so the backlog can be charted over time. Runs after retention, so the level it stores is the one the pipeline actually sits at.'],
  ['StorageSnapshot', 'aggregate', 'Storage Snapshot', 'Records the day\'s database size per table, feeding the disk-usage forecast. Runs last so it measures the size after retention, not before.'],
]

export const CATALOG: PipelineCatalogReadModel = {
  steps: STEPS.map(([name, lane, label, description]) => ({ name, jobMode: 'Full', lane, label, description })),
  lanes: [
    {
      id: 'fetch',
      label: 'Fetch lane',
      description: 'Everything that talks to Riot, paced by the per-region limiter: it reads the ladders, picks who to visit and downloads their matches.',
      jobModes: ['Full', 'FetchLane', 'FetchWorkers'],
      steps: STEPS.filter(([, lane]) => lane === 'fetch').map(([name]) => name),
    },
    {
      id: 'aggregate',
      label: 'Aggregate lane',
      description: 'Everything that only reads and writes Postgres: it folds the matches already downloaded into the stats the site serves, then prunes what is no longer needed.',
      jobModes: ['Full', 'AggregateLane'],
      steps: STEPS.filter(([, lane]) => lane === 'aggregate').map(([name]) => name),
    },
  ],
}

/** Every step name, in pipeline order. */
export const CHAIN: readonly string[] = STEPS.map(([name]) => name)
