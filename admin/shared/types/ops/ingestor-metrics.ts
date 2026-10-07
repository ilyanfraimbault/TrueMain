// The Ingestor's own meter — `GET /api/ops/ingestor-metrics` (#1636).
import type { RiotUsageWindow } from './riot-usage'

/** One tag set of an instrument, summed over the window. */
export interface IngestorMeterSeries {
  tags: Record<string, string>
  /** Measurements recorded. */
  count: number
  /** Their sum: a counter's total, a histogram's summed value. */
  sum: number
  /** `sum / count` — a histogram's mean measurement. */
  mean: number
  max: number
  lastRecordedAtUtc: string
}

/** One instrument of the `TrueMain.Ingestor` meter; series by total descending. */
export interface IngestorInstrument {
  name: string
  kind: 'counter' | 'histogram'
  unit: string | null
  description: string | null
  count: number
  sum: number
  max: number
  series: IngestorMeterSeries[]
}

/**
 * `GET /api/ops/ingestor-metrics` — every instrument the Ingestor recorded in the window.
 * An instrument absent from `instruments` recorded nothing in it. `oldestRetainedUtc` is
 * null when no rollup was ever written: nothing measured, not a quiet window.
 */
export interface IngestorMetrics {
  window: RiotUsageWindow
  sinceUtc: string
  generatedAtUtc: string
  retentionDays: number | null
  oldestRetainedUtc: string | null
  instruments: IngestorInstrument[]
}
