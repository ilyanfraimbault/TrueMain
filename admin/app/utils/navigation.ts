import type { CommandPaletteGroup, CommandPaletteItem, NavigationMenuItem } from '@nuxt/ui'
import NAVIGATION from './navigation.json'

// The destinations themselves live in `navigation.json`: one entry per sidebar
// group, each destination a `[label, icon, route]` tuple. A data table that
// regular is read as duplicated code by Sonar when written in TS.
type Destination = [label: string, icon: string, to: string]

interface NavigationGroup {
  label: string
  // The group's pages, one sidebar link each.
  pages: Destination[]
  // Pages that live behind a `?view=` / `?status=` tab rather than their own
  // route: the sidebar only carries the hub, so the palette (#1415) is where they
  // become reachable by name, under the same heading as their hub. The icons are
  // the ones each tab wears on its page.
  tabs: Destination[]
}

const GROUPS = NAVIGATION as NavigationGroup[]

function link([label, icon, to]: Destination) {
  return { label, icon, to }
}

// Four labelled groups (#1410) rather than one flat list of 15: the portal
// answers three questions — is ingestion flowing, is the data right, what is
// failing — and the sidebar should say so before the operator has to guess which
// page holds the answer. `UNavigationMenu` renders an array of arrays as groups
// with a divider between them; the group labels are the first item of each.
// The three account panels (candidates / explorer / add mains) are one hub with
// tabs, and Riot API is a tab of Processes — those routes still resolve, via
// redirect pages.
export const NAV_GROUPS: NavigationMenuItem[][] = GROUPS.map(group => [
  { label: group.label, type: 'label' },
  ...group.pages.map(link),
])

export const TAB_ENTRIES: Record<string, CommandPaletteItem[]> = Object.fromEntries(
  GROUPS.map(group => [group.label, group.tabs.map(link)]),
)

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
