/**
 * Keyboard activation for `UTable`'s selectable rows (#1734). A table with an
 * `@select` handler marks each row `role="button" tabindex="0"` but listens
 * only for clicks, so a keyboard user could focus a row and not open it. Bound
 * once on the table (`@keydown`), this turns Enter and Space on a focused row
 * into the click the table already handles. Keys pressed inside a row's own
 * controls (a link, the follow star) never reach here as the row's — their
 * target is the control, not the row.
 */
export function clickSelectableRow(event: KeyboardEvent): void {
  if (event.key !== 'Enter' && event.key !== ' ') return
  const target = event.target
  if (!(target instanceof HTMLElement) || !target.matches('tr[data-slot="tr"][role="button"]')) return
  event.preventDefault()
  target.click()
}

/** A click that asks for a new tab (Ctrl / Cmd / middle button) rather than in-place navigation. */
export function wantsNewTab(event: Event): boolean {
  return event instanceof MouseEvent && (event.metaKey || event.ctrlKey || event.button === 1)
}
