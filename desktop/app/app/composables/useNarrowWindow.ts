/**
 * The window is resizable (#1914). At its default width (1180) and above the
 * sidebar shows in full; below `NARROW_WIDTH` it folds into a rail of icons,
 * so the pages keep about the width they have at the default size down to the
 * window's minimum (`MIN_SIZE` in `crates/shell-state/src/window.rs`).
 *
 * One media query for the window's life, shared by every caller.
 */
export const NARROW_WIDTH = 1100

export function useNarrowWindow() {
  const narrow = useState<boolean>('narrow-window', () => false)
  const followed = useState<boolean>('narrow-window-followed', () => false)
  if (import.meta.client && !followed.value) {
    followed.value = true
    const query = window.matchMedia(`(max-width: ${NARROW_WIDTH - 1}px)`)
    narrow.value = query.matches
    query.addEventListener('change', event => (narrow.value = event.matches))
  }
  return narrow
}
