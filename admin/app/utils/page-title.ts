import type { DetectorStatus } from '~~/shared/types/ops'
import { GROUPS, type NavigationGroup } from './navigation'

// Browser-tab titles (#2296), read off the same table as the sidebar and the ⌘K
// palette (`navigation.json`), so a page or a tab added there is named in the tab
// strip for free and the three never disagree on what a destination is called.

export const APP_TITLE = 'TrueMain admin'

/** `%s · TrueMain admin`, or the bare app name when a page has no title. */
export function titleTemplate(title?: string | null): string {
  return title ? `${title} · ${APP_TITLE}` : APP_TITLE
}

export type RouteQuery = Record<string, unknown>

function splitRoute(to: string): { path: string, query: URLSearchParams } {
  const [path = '/', search = ''] = to.split('?')
  return { path, query: new URLSearchParams(search) }
}

function matchesQuery(expected: URLSearchParams, query: RouteQuery): boolean {
  return [...expected.entries()].every(([key, value]) => query[key] === value)
}

/**
 * The title of the destination a route lands on: a tab when every query
 * parameter of one of its entries matches (`Processes → Riot API` reads
 * `Processes — Riot API`), else the page owning the path. `undefined` for a route
 * the table does not name (login, redirects), which the template turns into the
 * bare app name.
 */
export function pageTitle(path: string, query: RouteQuery = {}, groups: NavigationGroup[] = GROUPS): string | undefined {
  for (const [label, , to] of groups.flatMap(group => group.tabs)) {
    const tab = splitRoute(to)
    if (tab.path === path && matchesQuery(tab.query, query)) {
      return label.replace(' → ', ' — ')
    }
  }
  const page = groups.flatMap(group => group.pages).find(([, , to]) => splitRoute(to).path === path)
  return page?.[0]
}

// Only the two alarming verdicts mark the tab: green is the resting state, and
// `unknown` means "not measured", which is neither a pass nor an alarm.
const VERDICT_WORD: Partial<Record<DetectorStatus, string>> = {
  amber: 'Amber',
  red: 'Red',
}

/** `● Red — Health` when the verdict is not green; the title unchanged otherwise. */
export function verdictTitle(title: string, status: string | null | undefined): string {
  const word = VERDICT_WORD[status as DetectorStatus]
  return word ? `● ${word} — ${title}` : title
}

/**
 * How often a background `/health` or Overview tab re-reads the verdict its
 * title shows: every other panel stops in a hidden tab (#1411), but a title that
 * never updates there would defeat the point. One small payload a minute.
 */
export const HIDDEN_VERDICT_REFRESH_MS = 60_000

export const DEFAULT_FAVICON = '/favicon.ico'

/** The favicon with a coloured dot for a non-green verdict, the plain one otherwise. */
export function verdictFavicon(status: string | null | undefined): { href: string, type?: string } {
  return VERDICT_WORD[status as DetectorStatus]
    ? { href: `/favicon-${status}.svg`, type: 'image/svg+xml' }
    : { href: DEFAULT_FAVICON }
}
