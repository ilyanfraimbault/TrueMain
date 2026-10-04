// Shared by the Accounts hub's card components (split out of `AccountsTrace.vue`
// and `AccountsPipeline.vue`, #1436).
import { formatDateTime, formatTimeAgo } from '~~/shared/utils/format'

// Both stamps in one cell: the absolute time answers "when", the relative one
// answers "is this recent", and an operator reading a stalled pipeline needs both.
export function traceStamp(iso: string | null | undefined): string {
  return iso ? `${formatDateTime(iso)} (${formatTimeAgo(iso)})` : '—'
}

// Riot ID display for candidate rows: "gameName#tagLine" when resolved, else an
// em dash.
export function candidateRiotIdLabel(gameName: string | null, tagLine: string | null): string {
  if (!gameName) {
    return '—'
  }
  return tagLine ? `${gameName}#${tagLine}` : gameName
}
