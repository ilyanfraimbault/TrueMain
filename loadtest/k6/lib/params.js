// Randomised request parameters for the load scenarios.
//
// The spread is the point: the API caches champion reads per slice (champion,
// position, rank bracket, patch), so a test that repeats one slice measures the
// cache and never the database behind it.

export const POSITIONS = ['TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY']

// Weighted toward the champion pages' own default, because most visitors never
// open the rank picker. Values follow the API's rule: `ALL`, one tier, or
// `<TIER>_PLUS`.
const ELO_BRACKETS = [
  ['MASTER_PLUS', 40],
  ['ALL', 15],
  ['DIAMOND_PLUS', 15],
  ['EMERALD_PLUS', 10],
  ['PLATINUM_PLUS', 10],
  ['GOLD', 5],
  ['CHALLENGER', 5],
]

export const DEFAULT_ELO_BRACKET = 'MASTER_PLUS'

export function pick(items) {
  return items[Math.floor(Math.random() * items.length)]
}

export function weighted(entries) {
  const total = entries.reduce((sum, entry) => sum + entry[1], 0)
  let roll = Math.random() * total
  for (const [value, weight] of entries) {
    roll -= weight
    if (roll < 0) return value
  }
  return entries[entries.length - 1][0]
}

export function pickPosition() {
  return pick(POSITIONS)
}

export function pickEloBracket() {
  return weighted(ELO_BRACKETS)
}

export function between(min, max) {
  return min + Math.random() * (max - min)
}

/** `?a=1&b=2` from an object, skipping empty values; `''` when nothing is left. */
export function query(params) {
  const parts = []
  for (const key of Object.keys(params)) {
    const value = params[key]
    if (value === undefined || value === null || value === '') continue
    parts.push(`${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`)
  }
  return parts.length > 0 ? `?${parts.join('&')}` : ''
}
