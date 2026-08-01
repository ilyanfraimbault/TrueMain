import type { ChampionSummaryResponse } from '~~/shared/types/champions'
import type { ProfileMainChampion } from '~~/shared/types/profile'

/**
 * Pure selection helpers behind the share cards (#926). Kept out of the
 * `/api/og/**` handlers (and well out of the Satori templates) so the one part
 * of the feature that can silently print the *wrong* real number — picking
 * which measured row the card speaks for — is unit-testable on its own.
 */

/**
 * Picks the directory row a champion card should print.
 *
 * `GET /champions` returns one row per (champion, position), so a champion
 * played in two lanes has two legitimately different win rates. The pinned lane
 * from the shared URL wins when it exists; otherwise we take the most-played
 * row, which is the slice `GET /champions/{id}` defaults to — so the card shows
 * what the page shows.
 *
 * A pinned lane with no row falls back to the most-played one *on purpose*, and
 * this is safe only because the card always captions the row's own
 * `position`, never the requested one: it says "Middle" and prints Middle's
 * numbers. That also mirrors the page, where `useChampion`'s 404 fallback drops
 * a dead lane filter and the URL reconciler then clears it — a card that went
 * blank here would contradict a page that renders fine.
 *
 * Returns null only when the champion has no row at all; the caller degrades
 * the whole stats block rather than substituting another champion's numbers.
 */
export function selectChampionSummaryRow(
  summaries: readonly ChampionSummaryResponse[] | null | undefined,
  championId: number,
  position?: string | null,
): ChampionSummaryResponse | null {
  if (!summaries?.length) return null

  const rows = summaries.filter(row => row.championId === championId)
  if (rows.length === 0) return null

  if (position) {
    const wanted = position.toUpperCase()
    const pinned = rows.find(row => row.position?.toUpperCase() === wanted)
    if (pinned) return pinned
  }

  // `reduce` rather than `sort` — no copy, and a stable "first row wins" on a
  // tie, so two equally played lanes always resolve to the same card.
  return rows.reduce((best, row) => (row.games > best.games ? row : best))
}

/**
 * Picks the champion a profile card speaks for: the one the player has the most
 * games on, which is what the profile page shows first and what the dedication
 * score is computed against when unfiltered.
 *
 * Returns null for a player whose main analysis has classified nothing yet —
 * the card then simply has no champion block.
 */
export function selectSignatureMain(
  mains: readonly ProfileMainChampion[] | null | undefined,
): ProfileMainChampion | null {
  if (!mains?.length) return null
  return mains.reduce((best, main) => (main.games > best.games ? main : best))
}

/**
 * Champion ids are the one card input that reaches us straight from a URL
 * segment. Anything that is not a plain positive integer is rejected outright
 * rather than coerced, so a hand-crafted OG URL can never turn into a
 * `/champions/NaN` fan-out against the backend.
 */
export function parseOgChampionId(raw: string | null | undefined): number | null {
  if (!raw || !/^\d{1,7}$/.test(raw)) return null
  const id = Number(raw)
  return id > 0 ? id : null
}
