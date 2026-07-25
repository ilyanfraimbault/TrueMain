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
 * One-word verdict bands for a score. Presentation-only: they exist so a
 * reader can place a number without knowing the scale, and they never feed
 * back into ordering. `colorClass` reuses the app's S/A/B/C/D performance-tier
 * palette (see `--color-tier-*` in main.css, already worn by `TierBadge`) so
 * the same rose-gold→iron read that means "best→worst" elsewhere in the app
 * means the same thing here, without inventing a second colour language.
 */
const DEDICATION_TIERS = [
  { min: 85, label: 'Devoted', colorClass: 'text-tier-s' },
  { min: 70, label: 'Committed', colorClass: 'text-tier-a' },
  { min: 50, label: 'Invested', colorClass: 'text-tier-b' },
  { min: 30, label: 'Casual', colorClass: 'text-tier-c' },
  { min: 0, label: 'Dabbling', colorClass: 'text-tier-d' },
] as const

function dedicationTierEntry(score: number): typeof DEDICATION_TIERS[number] {
  // The `min: 0` band matches any score >= 0, so the fallback below is only
  // reachable for a negative score (unexpected, but not worth widening the
  // return type over) — non-null because the last array element always exists.
  return DEDICATION_TIERS.find(tier => score >= tier.min) ?? DEDICATION_TIERS[DEDICATION_TIERS.length - 1]!
}

/** A one-word verdict for a score — see {@link DEDICATION_TIERS}. */
export function dedicationTier(score: number): string {
  return dedicationTierEntry(score).label
}

/** Tailwind text-colour utility for a score's tier — see {@link DEDICATION_TIERS}. */
export function dedicationTierColor(score: number): string {
  return dedicationTierEntry(score).colorClass
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
      // Labelled "Play rate" rather than "Commitment" so it reads distinctly
      // from the parent "Dedication" score it feeds into — the two words were
      // near-synonyms, which made the breakdown confusing rather than
      // explanatory. The field name (`commitment`) and its weight are
      // unchanged; this is a display label only.
      label: 'Play rate',
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
