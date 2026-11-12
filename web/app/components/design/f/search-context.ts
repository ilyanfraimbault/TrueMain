import type { InjectionKey } from 'vue'

/**
 * Variant F ("Dock"): the shell owns the one `AppSearch` instance (the dock's
 * search tile, which also owns ⌘K). Anything inside the shell opens that
 * palette through this — the home's champion field hands it the term it could
 * not match, so a player name typed there carries over to the player search.
 */
export interface DockSearch {
  open: (term?: string) => void
}

export const DOCK_SEARCH_KEY: InjectionKey<DockSearch> = Symbol('f-dock-search')

/** Whether an event target takes text (keyboard shortcuts must leave it alone). */
export function isEditableTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false
  if (target.isContentEditable) return true
  const tag = target.tagName
  if (tag === 'TEXTAREA' || tag === 'SELECT') return true
  if (tag !== 'INPUT') return false
  const type = (target as HTMLInputElement).type
  return !['button', 'checkbox', 'radio', 'range', 'submit', 'reset', 'color', 'file'].includes(type)
}

/** A dialog (the palette, a select's popover…) is open: global keys stay out of its way. */
export function dialogOpen(): boolean {
  return document.querySelector('[role="dialog"][data-state="open"], [role="listbox"][data-state="open"], [role="menu"][data-state="open"]') !== null
}

// Nuxt registers every file under `components/` as a component; a default export
// keeps that registration well-typed. Import the named exports.
export default DOCK_SEARCH_KEY
