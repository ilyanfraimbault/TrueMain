// Engine labels for the Database panel's cards (split out of `pages/database.vue`, #1436).
import type { StorageEngine } from '~~/shared/types/ops'

// Two engines can carry the same name (process_runs, seed_requests were both a
// Postgres table and a Mongo collection until #1244), so rows are keyed and labelled
// by engine rather than by name alone.
// Indexed as a loose record on purpose: an engine the API grows later should read
// as its own raw name rather than as an empty cell.
const ENGINE_LABELS: Record<string, string> = {
  postgres: 'Postgres',
  mongo: 'Mongo',
}

export function storageEngineLabel(engine: StorageEngine): string {
  return ENGINE_LABELS[engine] ?? engine
}
