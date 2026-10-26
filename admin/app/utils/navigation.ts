import type { CommandPaletteGroup, CommandPaletteItem, NavigationMenuItem } from '@nuxt/ui'

// Four labelled groups (#1410) rather than one flat list of 15: the portal
// answers three questions — is ingestion flowing, is the data right, what is
// failing — and the sidebar should say so before the operator has to guess which
// page holds the answer. `UNavigationMenu` renders an array of arrays as groups
// with a divider between them; the group labels are the first item of each.
// The three account panels (candidates / explorer / add mains) are one hub with
// tabs, and Riot API is a tab of Processes — those routes still resolve, via
// redirect pages.
export const NAV_GROUPS: NavigationMenuItem[][] = [
  [
    { label: 'Monitor', type: 'label' },
    { label: 'Overview', icon: 'i-lucide-layout-dashboard', to: '/' },
    { label: 'Health', icon: 'i-lucide-heart-pulse', to: '/health' },
    { label: 'Processes', icon: 'i-lucide-activity', to: '/processes' },
    { label: 'Logs', icon: 'i-lucide-scroll-text', to: '/logs' },
  ],
  [
    { label: 'Data', type: 'label' },
    { label: 'Champions', icon: 'i-lucide-swords', to: '/champions' },
    { label: 'Aggregation', icon: 'i-lucide-combine', to: '/aggregation' },
    { label: 'Patch Coverage', icon: 'i-lucide-layers', to: '/patch-coverage' },
    { label: 'Data Quality', icon: 'i-lucide-shield-alert', to: '/data-quality' },
  ],
  [
    { label: 'Accounts', type: 'label' },
    { label: 'Accounts', icon: 'i-lucide-users-round', to: '/accounts' },
    { label: 'Known players', icon: 'i-lucide-badge-check', to: '/known-players' },
  ],
  [
    { label: 'System', type: 'label' },
    { label: 'Database', icon: 'i-lucide-database', to: '/database' },
    { label: 'Configuration', icon: 'i-lucide-settings', to: '/configuration' },
    { label: 'Analytics', icon: 'i-lucide-chart-line', to: '/analytics' },
    { label: 'Desktop app', icon: 'i-lucide-monitor-down', to: '/desktop' },
  ],
]

// Pages that live behind a `?view=` / `?status=` tab rather than their own route:
// the sidebar only carries the hub, so the palette (#1415) is where they become
// reachable by name. Keyed by the sidebar group they belong to so each lands under
// the same heading as its hub; the icons are the ones each tab wears on its page.
export const TAB_ENTRIES: Record<string, CommandPaletteItem[]> = {
  Monitor: [
    {
      label: 'Processes → Riot API',
      icon: 'i-lucide-radio-tower',
      to: '/processes?view=riot-api',
    },
    { label: 'Processes → Scrapers', icon: 'i-lucide-bot', to: '/processes?view=scrapers' },
    { label: 'Logs → Crashes', icon: 'i-lucide-skull', to: '/logs?view=crashes' },
  ],
  Accounts: [
    {
      label: 'Accounts → Pipeline',
      icon: 'i-lucide-users-round',
      to: '/accounts?view=pipeline',
    },
    {
      label: 'Accounts → Trace',
      icon: 'i-lucide-user-search',
      to: '/accounts?view=trace',
    },
    {
      label: 'Accounts → Add mains',
      icon: 'i-lucide-user-plus',
      to: '/accounts?view=seed',
    },
    {
      label: 'Known players → Open conflicts',
      icon: 'i-lucide-git-pull-request-arrow',
      to: '/known-players?status=open',
    },
    {
      label: 'Known players → Resolved conflicts',
      icon: 'i-lucide-check-check',
      to: '/known-players?status=resolved',
    },
    {
      label: 'Known players → Homonyms',
      icon: 'i-lucide-copy',
      to: '/known-players?status=homonyms',
    },
  ],
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
