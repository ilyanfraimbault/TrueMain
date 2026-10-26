import type { HealthAlerts } from '~~/shared/types/ops'

/**
 * `GET /api/ops/health-alerts` (#2292) — whether the pipeline-health alerter is on, the
 * incidents it holds open, and the last alerts it sent.
 */
export function useHealthAlerts() {
  return useOps<HealthAlerts>('/health-alerts')
}
