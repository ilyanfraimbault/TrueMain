// Presentation and verdict types shared by several ops areas.

/** Nuxt UI badge/icon color used by the admin panels' status/severity badges. */
export type BadgeColor = 'error' | 'warning' | 'info' | 'neutral' | 'success' | 'primary'

/**
 * A detector verdict. `unknown` means the measurement could not be taken, never
 * "measured and fine" — only a real check turns a card green.
 */
export type DetectorStatus = 'green' | 'amber' | 'red' | 'unknown'
