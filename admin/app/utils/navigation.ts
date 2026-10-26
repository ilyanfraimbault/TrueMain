import type { CommandPaletteGroup, CommandPaletteItem, NavigationMenuItem } from '@nuxt/ui'

// One destination: label, icon, route. Declared as tuples so the tables below
// stay one line per destination.
type Destination = readonly [label: string, icon: string, to: string]

function link([label, icon, to]: Destination) {
  return { label, icon, to }
}

function navGroup(label: string, pages: Destination[]): NavigationMenuItem[] {
  return [{ label, type: 'label' }, ...pages.map(link)]
}

// Four labelled groups (#1410) rather than one flat list of 15: the portal
// answers three questions — is ingestion flowing, is the data right, what is
// failing — and the sidebar should say so before the operator has to guess which
// page holds the answer. `UNavigationMenu` renders an array of arrays as groups
// with a divider between them; the group labels are the first item of each.
// The three account panels (candidates / explorer / add mains) are one hub with
// tabs, and Riot API is a tab of Processes — those routes still resolve, via
// redirect pages.
export const NAV_GROUPS: NavigationMenuItem[][] = [
  navGroup('Monitor', [
    ['Overview', 'i-lucide-layout-dashboard', '/'],
    ['Health', 'i-lucide-heart-pulse', '/health'],
    ['Processes', 'i-lucide-activity', '/processes'],
    ['Logs', 'i-lucide-scroll-text', '/logs'],
  ]),
  navGroup('Data', [
    ['Champions', 'i-lucide-swords', '/champions'],
    ['Aggregation', 'i-lucide-combine', '/aggregation'],
    ['Patch Coverage', 'i-lucide-layers', '/patch-coverage'],
    ['Data Quality', 'i-lucide-shield-alert', '/data-quality'],
  ]),
  navGroup('Accounts', [
    ['Accounts', 'i-lucide-users-round', '/accounts'],
    ['Known players', 'i-lucide-badge-check', '/known-players'],
  ]),
  navGroup('System', [
    ['Database', 'i-lucide-database', '/database'],
    ['Configuration', 'i-lucide-settings', '/configuration'],
    ['Analytics', 'i-lucide-chart-line', '/analytics'],
    ['Desktop app', 'i-lucide-monitor-down', '/desktop'],
  ]),
]

const MONITOR_TABS: Destination[] = [
  ['Processes → Riot API', 'i-lucide-radio-tower', '/processes?view=riot-api'],
  ['Processes → Scrapers', 'i-lucide-bot', '/processes?view=scrapers'],
  ['Logs → Crashes', 'i-lucide-skull', '/logs?view=crashes'],
]

const ACCOUNTS_TABS: Destination[] = [
  ['Accounts → Pipeline', 'i-lucide-users-round', '/accounts?view=pipeline'],
  ['Accounts → Trace', 'i-lucide-user-search', '/accounts?view=trace'],
  ['Accounts → Add mains', 'i-lucide-user-plus', '/accounts?view=seed'],
  ['Known players → Open conflicts', 'i-lucide-git-pull-request-arrow', '/known-players?status=open'],
  ['Known players → Resolved conflicts', 'i-lucide-check-check', '/known-players?status=resolved'],
  ['Known players → Homonyms', 'i-lucide-copy', '/known-players?status=homonyms'],
]

// Pages that live behind a `?view=` / `?status=` tab rather than their own route:
// the sidebar only carries the hub, so the palette (#1415) is where they become
// reachable by name. Keyed by the sidebar group they belong to so each lands under
// the same heading as its hub; the icons are the ones each tab wears on its page.
export const TAB_ENTRIES: Record<string, CommandPaletteItem[]> = {
  Monitor: MONITOR_TABS.map(link),
  Accounts: ACCOUNTS_TABS.map(link),
}

// ⌘K palette (#1415): jumping by name beats hunting the sidebar. Same groups,
// same order, plus the tabbed destinations.
export function buildSearchGroups(
  navGroups: NavigationMenuItem[][] = NAV_GROUPS,
  tabEntries: Record<string, CommandPaletteItem[]> = TAB_ENTRIES,
): CommandPaletteGroup<CommandPaletteItem>[] {
  return navGroups.map((group) => {
    const label = String(group.find(item => item.type === 'label')?.label ?? '')
    return {
      id: label.toLowerCase(),
      label,
      items: [
        ...group
          .filter(item => item.type !== 'label')
          .map(item => ({
            label: String(item.label),
            icon: item.icon,
            to: item.to,
          } satisfies CommandPaletteItem)),
        ...(tabEntries[label] ?? []),
      ],
    }
  })
}
