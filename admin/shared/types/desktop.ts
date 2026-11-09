// `GET /api/ops/desktop/usage` (#1805): the desktop app's downloads and usage.
// Its own file rather than `ops.ts`, which is past the size limit and may only shrink.

/** One row of a ranked list on the Desktop app page. */
export interface DesktopShareRow {
  key: string
  label: string
  value: number
  /** A second figure in words, e.g. "4 installs". */
  hint?: string
}
