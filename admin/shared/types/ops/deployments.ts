// `GET /api/ops/deployments` (#2297): when each backend process booted a new version, from
// the append-only `process_boots` collection every Api and Ingestor writes at startup. A
// restart on the same version is not listed.

export interface Deployment {
  /** `Api` or `Ingestor`. */
  processName: string
  /** Which container of the process (the ingestor's job mode); null for the Api. */
  role: string | null
  version: string
  /** The version it replaced; null on the process's first recorded boot. */
  previousVersion: string | null
  /** ISO timestamp of the boot on the new version: the rollout time. */
  deployedAtUtc: string
}

export interface Deployments {
  fromUtc: string
  toUtc: string
  /** Oldest first. */
  deployments: Deployment[]
}
