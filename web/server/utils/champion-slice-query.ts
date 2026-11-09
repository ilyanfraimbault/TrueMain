import type { H3Event } from 'h3'

/**
 * The champion slice a public server route was asked for, read from its query.
 *
 * Shape guards, not semantic validation: an unknown lane or a nonsense bracket
 * fails to match a slice and degrades the answer. Their real job is bounding the
 * *cache key* space, since these routes are publicly reachable and each distinct
 * key costs one backend call.
 */
export interface ChampionSliceQuery {
  patch: string | null
  eloBracket: string | null
  position: string | null
}

const PATCH_RE = /^\d{1,3}\.\d{1,3}$/
const ELO_BRACKET_RE = /^[A-Z_]{1,20}$/
const POSITION_RE = /^[A-Z]{1,10}$/

/**
 * A champion id straight from a URL segment: a plain positive integer of at most
 * seven digits, rejected rather than coerced, so a hand-crafted URL can never
 * turn into a `/champions/NaN` fan-out against the backend.
 */
export function readChampionId(raw: string | null | undefined): number | null {
  if (!raw || !/^\d{1,7}$/.test(raw)) return null
  const id = Number(raw)
  return id > 0 ? id : null
}

export function readChampionSliceQuery(event: H3Event): ChampionSliceQuery {
  const query = getQuery(event) as Record<string, unknown>
  const pick = (key: string, pattern: RegExp): string | null => {
    const raw = query[key]
    if (typeof raw !== 'string') return null
    // Digits and a dot survive `toUpperCase()` unchanged, so the patch shares it.
    const value = raw.trim().toUpperCase()
    return pattern.test(value) ? value : null
  }
  return {
    patch: pick('patch', PATCH_RE),
    eloBracket: pick('eloBracket', ELO_BRACKET_RE),
    position: pick('position', POSITION_RE),
  }
}
