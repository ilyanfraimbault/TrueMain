// The day a match (or a measurement) happened, as the match-history headings
// print it: `11 Aug`, widened to `11 Aug 2024` outside the current year so a
// two-season-deep history can't show two identical headings.

// Shared formatter instances — rebuilding Intl.DateTimeFormat per row is
// measurable on a long feed (same reason as the relative-time formatter).
const dayFormatter = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short' })
const dayWithYearFormatter = new Intl.DateTimeFormat('en-GB', {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
})

/** Empty when the timestamp doesn't parse, so the caller can show no date rather than a bogus one. */
export function formatMatchDay(isoTimestamp: string, now: Date = new Date()): string {
  const date = new Date(isoTimestamp)
  if (Number.isNaN(date.getTime())) return ''
  return date.getFullYear() === now.getFullYear()
    ? dayFormatter.format(date)
    : dayWithYearFormatter.format(date)
}
