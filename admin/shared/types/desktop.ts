// `GET /api/ops/desktop/usage` (#1805): the desktop app's downloads and usage.
// Its own file rather than `ops.ts`, which is past the size limit and may only shrink.

export interface DesktopActiveInstalls {
  today: number
  last7Days: number
  last30Days: number
}

export interface DesktopUsageTotals {
  installs: number
  newInstalls: number
  launches: number
  openMinutes: number
  activeInstallDays: number
  downloadsMac: number
  downloadsWindows: number
}

export interface DesktopUsageDayBucket {
  /** ISO-8601, midnight UTC. */
  dayUtc: string
  activeInstalls: number
  newInstalls: number
  launches: number
  openMinutes: number
  downloadsMac: number
  downloadsWindows: number
}

export interface DesktopShare {
  key: string
  installs: number
}

export interface DesktopDownloadVersion {
  version: string
  mac: number
  windows: number
}

export interface DesktopKeyUsage {
  key: string
  count: number
  installs: number
}

export interface DesktopUsage {
  windowDays: number
  usageRetentionDays: number
  earliestDayUtc: string | null
  active: DesktopActiveInstalls
  totals: DesktopUsageTotals
  days: DesktopUsageDayBucket[]
  versions: DesktopShare[]
  operatingSystems: DesktopShare[]
  downloadVersions: DesktopDownloadVersion[]
  pages: DesktopKeyUsage[]
  features: DesktopKeyUsage[]
}

/** One row of a ranked list on the Desktop app page. */
export interface DesktopShareRow {
  key: string
  label: string
  value: number
  /** A second figure in words, e.g. "4 installs". */
  hint?: string
}
