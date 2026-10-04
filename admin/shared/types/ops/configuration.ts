// Effective configuration viewer — `GET /api/ops/configuration` (#1034).

/**
 * Where a bound value came from (#1034). `default` — no provider supplies the key,
 * the value is the class default. `override` — a provider supplies it; `source`
 * names which one. `derived` — no provider supplies it, yet the value differs from
 * the class default, so something computed it at boot.
 */
export type EffectiveConfigurationOrigin = 'default' | 'override' | 'derived'

/** How to read an effective-configuration value's number. */
export type EffectiveConfigurationUnit = 'bytes' | 'duration' | 'count' | 'percent' | 'flag' | 'list' | 'text'

/** One bound option, as the process holds it. */
export interface EffectiveConfigurationValue {
  /** Fully-qualified configuration key, e.g. `StorageHistory:DiskCapacityBytes`. */
  key: string
  /** The property name alone, e.g. `DiskCapacityBytes`. */
  name: string
  /** The pasteable-back-into-configuration form. Null when the option is unset. */
  value: string | null
  /** The humanised form ("90 days", "1.0 TB"), or null when it would repeat `value`. */
  valueLabel: string | null
  origin: EffectiveConfigurationOrigin
  /** Which provider supplied an override, e.g. `environment`. Null for `default`/`derived`. */
  source: string | null
  unit: EffectiveConfigurationUnit
  /** Set when the value is unset and that has a visible consequence elsewhere in the portal. */
  notice: string | null
}

/** One configuration section's worth of values, with the prose explaining what it drives. */
export interface EffectiveConfigurationSection {
  /** The configuration key prefix, e.g. `StorageHistory`. */
  name: string
  title: string
  description: string
  values: EffectiveConfigurationValue[]
}

/** One process's snapshot: which build, which environment, and its sections. */
export interface EffectiveConfigurationProcess {
  /** Which host bound these values — `Api` or `Ingestor`. */
  processName: string
  environment: string
  /** The build this process is running, or null for a plain local build. */
  version: string | null
  /**
   * When this snapshot was taken. For the Api this is always "now" — it is built
   * live on every request. For the Ingestor it is the boot time of its last run:
   * still what that process is running, even if older than the last deploy.
   */
  capturedAtUtc: string
  sections: EffectiveConfigurationSection[]
}

/** `GET /api/ops/configuration` — what every host is actually running with. */
export interface EffectiveConfigurationOverviewResponse {
  processes: EffectiveConfigurationProcess[]
}
