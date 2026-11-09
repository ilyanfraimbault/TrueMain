import type { BadgeColor } from '~~/shared/types/ops'
import type { HealthAlertReadModel, HealthAlertsReadModel } from '#site-shared/contract/api'

/** How one sent alert is drawn: a refused delivery reads as such before its kind. */
export function healthAlertBadge(alert: Pick<HealthAlertReadModel, 'kind' | 'delivered'>): { label: string, color: BadgeColor } {
  if (!alert.delivered) {
    return { label: 'Not delivered', color: 'warning' }
  }
  return alert.kind === 'failing'
    ? { label: 'Failing', color: 'error' }
    : { label: 'Recovered', color: 'success' }
}

/** The alerter's rules as one sentence, so the panel says what earns a message. */
export function healthAlertingRules(alerts: Pick<HealthAlertsReadModel,
  'evaluationIntervalSeconds' | 'redConfirmations' | 'cooldownMinutes' | 'processFailureThreshold'>): string {
  const minutes = alerts.evaluationIntervalSeconds / 60
  const every = Number.isInteger(minutes) && minutes >= 1
    ? `${minutes} min`
    : `${alerts.evaluationIntervalSeconds} s`
  const holds = alerts.redConfirmations === 1
    ? 'on its first red evaluation'
    : `after ${alerts.redConfirmations} red evaluations in a row`
  return `Evaluated every ${every}; a signal is announced ${holds}, a process at `
    + `${alerts.processFailureThreshold} failed runs in a row, at most once per `
    + `${alerts.cooldownMinutes} min each, and again when it recovers.`
}

/** `process:MatchIngestion` → `MatchIngestion`; a signal key stays as it is. */
export function healthAlertKeyLabel(key: string): string {
  return key.startsWith('process:') ? key.slice('process:'.length) : key
}
