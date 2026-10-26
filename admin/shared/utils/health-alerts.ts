import type { BadgeColor, HealthAlert, HealthAlerts } from '~~/shared/types/ops'

/** How one sent alert is drawn: a refused delivery reads as such before its kind. */
export function healthAlertBadge(alert: Pick<HealthAlert, 'kind' | 'delivered'>): { label: string, color: BadgeColor } {
  if (!alert.delivered) {
    return { label: 'Not delivered', color: 'warning' }
  }
  return alert.kind === 'failing'
    ? { label: 'Failing', color: 'error' }
    : { label: 'Recovered', color: 'success' }
}

/** The alerter's rules as one sentence, so the panel says what earns a message. */
export function healthAlertingRules(alerts: Pick<HealthAlerts,
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
