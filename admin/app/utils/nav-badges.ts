import type { BadgeProps, NavigationMenuItem } from '@nuxt/ui'
import type { NavSummary } from '~~/shared/types/ops'
// Explicit import rather than the `~~` auto-import: this module is unit tested
// outside the Nuxt context (`tests/utils/nav-badges.test.ts`).
import { detectorStatusMeta } from '~~/shared/utils/detector-status'

/** A sidebar badge: Nuxt UI's own props plus the accessible name it is read by. */
export type NavBadge = BadgeProps & { 'aria-label': string, 'title': string }

/**
 * What the sidebar flags (#2294), by route: the Known players queue as one count
 * (open conflicts plus homonym groups — the page's tabs give the split), and a
 * dot on Health in the verdict's own colour.
 *
 * Nothing at all for a summary that is missing — not fetched yet, or failed:
 * never a stale or invented count. Nothing for a zero, nor for a green verdict,
 * so a badge always means "look here". An `unknown` verdict gets its neutral dot
 * only when it was measured: with no recent evaluation held, the server says
 * `unknown` without having evaluated anything, and that is not a reading.
 */
export function navBadges(summary: NavSummary | null | undefined): Record<string, NavBadge> {
  const badges: Record<string, NavBadge> = {}
  if (!summary) {
    return badges
  }

  const pending = summary.openConflicts + summary.homonymGroups
  if (pending > 0) {
    const text = `${count(summary.openConflicts, 'open conflict')}, ${count(summary.homonymGroups, 'homonym group')}`
    badges['/known-players'] = {
      'label': pending,
      'color': 'warning',
      'variant': 'subtle',
      'aria-label': text,
      'title': text,
    }
  }

  const measured = summary.healthStatus !== 'unknown' || summary.healthEvaluatedAtUtc !== null
  if (summary.healthStatus !== 'green' && measured) {
    const meta = detectorStatusMeta(summary.healthStatus)
    const text = `Pipeline health: ${meta.label.toLowerCase()}`
    badges['/health'] = {
      'label': '',
      'class': `size-2 p-0 rounded-full ring-0 ${meta.dot}`,
      'aria-label': text,
      'title': text,
    }
  }
  return badges
}

function count(n: number, noun: string): string {
  return `${n} ${noun}${n === 1 ? '' : 's'}`
}

/** The sidebar groups with each link's badge, if it has one. */
export function withNavBadges(
  groups: NavigationMenuItem[][],
  badges: Record<string, NavBadge>,
): NavigationMenuItem[][] {
  return groups.map(group => group.map((item) => {
    const badge = typeof item.to === 'string' ? badges[item.to] : undefined
    return badge ? { ...item, badge } : item
  }))
}
