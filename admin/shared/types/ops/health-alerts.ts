// The pipeline-health alerter — `GET /api/ops/health-alerts` (#2292).

/** `failing` when an alert announces at least one failure, `recovered` otherwise. */
export type HealthAlertKind = 'failing' | 'recovered'

/** One signal or process an alert reports on. */
export interface HealthAlertChange {
  /** The cockpit signal key (`dataQuality`…) or `process:` plus the process name. */
  key: string
  title: string
  change: HealthAlertKind
  status: string
  headline: string
}

/** One alert the API sent (or failed to deliver). */
export interface HealthAlert {
  id: string
  sentAtUtc: string
  kind: HealthAlertKind
  title: string
  /** The cockpit verdict when it was sent. */
  verdict: string
  channel: string
  delivered: boolean
  /** Why delivery failed — a status code or an exception type. */
  error: string | null
  changes: HealthAlertChange[]
}

/** The alerter's settings, open incidents and newest alerts, for the `/health` panel. */
export interface HealthAlerts {
  /** False when no webhook is configured: the alerter is off. */
  enabled: boolean
  channel: string | null
  evaluationIntervalSeconds: number
  redConfirmations: number
  cooldownMinutes: number
  processFailureThreshold: number
  lastEvaluatedAtUtc: string | null
  /** Keys with an incident open right now. */
  openIncidents: string[]
  /** Newest first. */
  alerts: HealthAlert[]
}
