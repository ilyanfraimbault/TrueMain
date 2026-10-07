import { shallowReactive } from 'vue'

/**
 * Load state of a sprite sheet, shared by every icon cut from it (#999).
 *
 * A rune tree draws ~30 icons from one sheet; each must show its skeleton until
 * the sheet is in, then all of them at once. A CSS background reports no `load`,
 * so the sheet is loaded once here through an `Image`, and the icons read the
 * outcome. Client-only: the server renders every icon as loading, which is also
 * what the client's first render shows, so hydration agrees.
 */
export type SheetImageState = 'loading' | 'loaded' | 'failed'

const states = shallowReactive(new Map<string, SheetImageState>())

/** Starts loading `url` once per page lifetime; later calls are no-ops. */
export function loadSheetImage(url: string): void {
  if (import.meta.server || states.has(url)) return
  states.set(url, 'loading')
  const image = new Image()
  image.onload = () => states.set(url, 'loaded')
  image.onerror = () => states.set(url, 'failed')
  image.src = url
}

export function sheetImageState(url: string): SheetImageState {
  return states.get(url) ?? 'loading'
}
