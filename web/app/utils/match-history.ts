import type { MatchSummaryResponse } from '~~/shared/types/matches'

// Day grouping for the match-history lists. A page of 20 identically-shaped
// rows reads as one undifferentiated wall; players think in play sessions
// ("that Tuesday night"), so the list is cut on the local day boundary and
// each run gets a date heading.

export interface MatchDayGroup {
  /** Stable local-day key (`2026-08-11`) — the v-for key for the group. */
  key: string
  /**
   * Heading text: `11 Aug`, widened to `11 Aug 2024` outside the current
   * year so a two-season-deep history can't show two identical headings.
   * Empty when the timestamp didn't parse, in which case the caller renders
   * the rows with no heading rather than a bogus date.
   */
  label: string
  matches: MatchSummaryResponse[]
}

// Shared formatter instances — rebuilding Intl.DateTimeFormat per row is
// measurable on a long feed (same reason as the relative-time formatter).
const dayFormatter = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short' })
const dayWithYearFormatter = new Intl.DateTimeFormat('en-GB', {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
})

const UNKNOWN_DAY_KEY = 'unknown-day'

function localDayKey(date: Date): string {
  const month = `${date.getMonth() + 1}`.padStart(2, '0')
  const day = `${date.getDate()}`.padStart(2, '0')
  return `${date.getFullYear()}-${month}-${day}`
}

/**
 * Formats a match timestamp as a day heading — see {@link MatchDayGroup.label}.
 */
export function formatMatchDay(isoTimestamp: string, now: Date = new Date()): string {
  const date = new Date(isoTimestamp)
  if (Number.isNaN(date.getTime())) return ''
  return date.getFullYear() === now.getFullYear()
    ? dayFormatter.format(date)
    : dayWithYearFormatter.format(date)
}

/**
 * Splits an already-ordered match list into consecutive same-day runs. Order
 * is preserved as given (the API returns newest-first) — this never re-sorts,
 * so a list ordered by something other than time simply yields one group per
 * contiguous run, never a reshuffled history.
 */
export function groupMatchesByDay(
  matches: MatchSummaryResponse[],
  now: Date = new Date(),
): MatchDayGroup[] {
  const groups: MatchDayGroup[] = []

  for (const match of matches) {
    const date = new Date(match.gameStartTimeUtc)
    const valid = !Number.isNaN(date.getTime())
    const key = valid ? localDayKey(date) : UNKNOWN_DAY_KEY

    const current = groups[groups.length - 1]
    if (current && current.key === key) {
      current.matches.push(match)
      continue
    }

    groups.push({
      key,
      label: valid ? formatMatchDay(match.gameStartTimeUtc, now) : '',
      matches: [match],
    })
  }

  return groups
}
