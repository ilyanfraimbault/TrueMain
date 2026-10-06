import type { MaybeRefOrGetter } from 'vue'
import type {
  CrashesFilters,
  CrashesResponse,
  EffectiveConfigurationOverviewResponse,
  PipelineHealth,
  ProcessIterationsFilters,
  ProcessIterationsResponse,
  ProcessRunsFilters,
  ProcessRunsResponse,
  RiotApiUsage,
  RiotUsageFilters,
} from '~~/shared/types/ops'
import type { LogsFilters, LogsResponse } from '~~/shared/types/logs'

// Pipeline runtime reads of the ops API — process runs, logs, crashes, Riot usage,
// health and configuration — built on `useOps` (`useOps.ts`).

/**
 * `GET /api/ops/process-runs` — server-paginated runs (newest first) plus the
 * per-process rollup, which covers the full filtered set regardless of paging.
 * Pass a reactive getter so the table re-fetches when a filter or the page
 * changes.
 */
export function useProcessRuns(
  filters?: MaybeRefOrGetter<ProcessRunsFilters>,
) {
  return useOps<ProcessRunsResponse>(
    '/process-runs',
    filters ? () => ({ ...toValue(filters) }) : undefined,
  )
}

/**
 * `GET /api/ops/process-iterations` — recent pipeline iterations (newest first),
 * each carrying its ordered process runs. Feeds the chain view. Pass a reactive
 * getter so it re-fetches when the page changes.
 */
export function useProcessIterations(
  filters?: MaybeRefOrGetter<ProcessIterationsFilters>,
) {
  return useOps<ProcessIterationsResponse>(
    '/process-iterations',
    filters ? () => ({ ...toValue(filters) }) : undefined,
  )
}

/**
 * `GET /api/ops/logs` — server-paginated application logs, newest first. `level`
 * is a minimum-severity threshold; `search` is a case-insensitive match on
 * message/exception. Pass a reactive getter so the table re-fetches when a
 * filter or the page changes.
 */
export function useLogs(
  filters?: MaybeRefOrGetter<LogsFilters>,
) {
  return useOps<LogsResponse>(
    '/logs',
    filters ? () => ({ ...toValue(filters) }) : undefined,
  )
}

/**
 * `GET /api/ops/crashes` — server-paginated crash reports, newest first. Each row
 * carries the full report (exception chain, environment + memory snapshot, and the
 * recent log tail), so no separate detail request is needed. Pass a reactive getter
 * so the table re-fetches when a filter or the page changes.
 */
export function useCrashes(
  filters?: MaybeRefOrGetter<CrashesFilters>,
) {
  return useOps<CrashesResponse>(
    '/crashes',
    filters ? () => ({ ...toValue(filters) }) : undefined,
  )
}

/**
 * `GET /api/ops/riot-usage` — Riot API usage metrics over a relative window
 * (`1h`/`24h`/`7d`/`30d`): totals, per-endpoint breakdown, status-code histogram,
 * call-volume time-series and the latest rate-limit snapshot. Pass a reactive
 * getter so the panel re-fetches when the window or endpoint filter changes.
 */
export function useRiotUsage(
  filters?: MaybeRefOrGetter<RiotUsageFilters>,
) {
  return useOps<RiotApiUsage>(
    '/riot-usage',
    filters ? () => ({ ...toValue(filters) }) : undefined,
  )
}

/**
 * `GET /api/ops/pipeline-health` — the health cockpit's single payload (#1031): one
 * rolled-up verdict, one line per signal, and the raw measurements behind them. Takes
 * no filters; the verdict and its thresholds are server-side, so the cockpit and the
 * panels it links to cannot drift apart.
 */
export function usePipelineHealth() {
  return useOps<PipelineHealth>('/pipeline-health')
}

/**
 * `GET /api/ops/configuration` — what every host is actually running with
 * (#1034): the Api's own options, read live, plus the Ingestor's, published to
 * Mongo at its own boot. Takes no filters.
 */
export function useEffectiveConfiguration() {
  return useOps<EffectiveConfigurationOverviewResponse>('/configuration')
}
