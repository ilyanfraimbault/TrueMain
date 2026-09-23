/**
 * Keyboard focus across client-side navigations (#1616).
 *
 * After hydration the browser never reloads the document, so it never resets
 * focus either: activating a header link leaves focus on that link while the
 * content below it is replaced. `NuxtRouteAnnouncer` covers the announcement;
 * this covers the keyboard position.
 */

/** `id` of the `<main>` region (`UMain` in `app.vue`), the skip link's target. */
export const MAIN_CONTENT_ID = 'main-content'

interface RouteLike {
  path: string
  matched: readonly unknown[]
}

/**
 * Whether a finished navigation should move focus to the main region.
 *
 * - Not on the initial navigation (`from` is the router's start location, which
 *   matches nothing): a page load must leave focus where the browser put it.
 * - Not when only the query or hash changed: the champion page's filters, the
 *   leaderboard's pager and the player page's match filters are all same-path
 *   navigations, and moving focus there would pull it out of the control the
 *   reader is still using.
 */
export function shouldMoveFocus(to: RouteLike, from: RouteLike): boolean {
  if (from.matched.length === 0) return false
  return to.path !== from.path
}

/**
 * Focus the main region. The element carries `tabindex="-1"` so it can take
 * focus programmatically without becoming a tab stop; the next Tab then lands
 * on the first focusable element inside the content.
 */
export function focusMainContent(options?: FocusOptions): boolean {
  const main = document.getElementById(MAIN_CONTENT_ID)
  if (!main) return false
  main.focus(options)
  return document.activeElement === main
}
