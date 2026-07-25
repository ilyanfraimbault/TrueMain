import type { TruemainDedication } from '~~/shared/types/dedication'

// Presentation helpers for the dedication score. Formatting only — the score
// and every component are computed by the backend
// (backend/Core/Truemains/DedicationScore.cs) and shipped in the payload, so
// nothing here derives a number the API didn't send. See docs/dedication-score.md.

/** The four components, in the order the UI lists them (heaviest weight first). */
export interface DedicationComponent {
  key: 'commitment' | 'span' | 'volume' | 'recency'
  label: string
  /** Normalised 0..1 value straight from the payload. */
  value: number
  /** The raw figure behind the component, phrased for a human. */
  detail: string
}

/**
 * The score as displayed: no decimal point. The backend already rounds to one
 * decimal, so this only drops the tenths for the compact leaderboard cell —
 * the tooltip and the profile card keep the precise value.
 */
export function formatDedicationScore(score: number): string {
  return Math.round(score).toString()
}

/**
 * A one-word verdict for a score. Bands are presentation-only: they exist so a
 * reader can place a number without knowing the scale, and they never feed back
 * into ordering.
 */
export function dedicationTier(score: number): string {
  if (score >= 85) return 'Devoted'
  if (score >= 70) return 'Committed'
  if (score >= 50) return 'Invested'
  if (score >= 30) return 'Casual'
  return 'Dabbling'
}

/** Days-since-last-game phrased for a human; null when nothing is tracked yet. */
export function formatDedicationLastPlayed(days: number | null): string {
  if (days === null) return 'no tracked game yet'
  if (days <= 0) return 'played today'
  if (days === 1) return 'played yesterday'
  return `played ${days} days ago`
}

/** The four components with their raw inputs, ready to render as bars or a list. */
export function dedicationComponents(dedication: TruemainDedication): DedicationComponent[] {
  return [
    {
      key: 'commitment',
      label: 'Commitment',
      value: dedication.commitment,
      detail: `${(dedication.playRate * 100).toFixed(0)}% of recent ranked games`,
    },
    {
      key: 'span',
      label: 'Span',
      value: dedication.span,
      detail: dedication.patchSpan === 1
        ? '1 tracked patch'
        : `${dedication.patchSpan} tracked patches`,
    },
    {
      key: 'volume',
      label: 'Volume',
      value: dedication.volume,
      detail: `${dedication.careerGames.toLocaleString('en-US')} tracked games`,
    },
    {
      key: 'recency',
      label: 'Recency',
      value: dedication.recency,
      detail: formatDedicationLastPlayed(dedication.daysSinceLastGame),
    },
  ]
}

/**
 * Plain-text breakdown for a `title` tooltip — used where a rich popover would
 * fight the row's stretched profile link.
 */
export function describeDedication(dedication: TruemainDedication, championName: string): string {
  const header = `Dedication ${dedication.score.toFixed(1)}/100 · ${championName}`
  const lines = dedicationComponents(dedication)
    .map(component => `${component.label} ${Math.round(component.value * 100)}% — ${component.detail}`)
  return [header, ...lines].join('\n')
}
