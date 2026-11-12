/** One row of a ranked list on the Desktop app page. */
export interface DesktopShareRow {
  key: string
  label: string
  value: number
  /** A second figure in words, e.g. "4 installs". */
  hint?: string
}
