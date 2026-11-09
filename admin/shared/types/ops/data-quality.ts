// Data-quality checks and detectors — `GET /api/ops/data-quality/*`.

import type { BadgeColor } from './common'

/**
 * Presentation metadata for one issue type — label, icon and badge color. Drives
 * the filter select, group headers and badges so the panel stays consistent.
 * Keyed by `DataQualityIssueType` in `ISSUE_META` on the data-quality page.
 */
export interface IssueMeta {
  label: string
  icon: string
  color: BadgeColor
  description: string
}
