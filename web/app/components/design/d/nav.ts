/**
 * Variant D ("Cockpit") — the sidebar's destinations and the context bar's
 * trail, both read from the route alone, so the server render and the client's
 * first render always agree.
 */

export interface NavItem {
  label: string
  icon: string
  to: string
  active: boolean
  badge?: string
}

export interface NavGroup {
  /** Section heading; none for the first group. */
  label?: string
  items: NavItem[]
}

function under(path: string, prefix: string): boolean {
  return path === prefix || path.startsWith(`${prefix}/`)
}

/** Same destinations as `AppHeader`, grouped the way an app's sidebar reads. */
export function navGroups(path: string): NavGroup[] {
  return [
    {
      items: [
        { label: 'Overview', icon: 'i-lucide-layout-dashboard', to: '/', active: path === '/' },
      ],
    },
    {
      label: 'Champions',
      items: [
        {
          label: 'Champions',
          icon: 'i-lucide-swords',
          to: '/champions',
          // Only one of the two champion entries lights at a time.
          active: under(path, '/champions') && !under(path, '/champions/tierlist'),
        },
        { label: 'Tier List', icon: 'i-lucide-trending-up', to: '/champions/tierlist', active: under(path, '/champions/tierlist') },
        { label: 'Matchup', icon: 'i-lucide-wand-sparkles', to: '/matchup', active: under(path, '/matchup') },
      ],
    },
    {
      label: 'Players',
      items: [
        // Exact match: a player's own profile does not light the leaderboard.
        { label: 'Truemains', icon: 'i-lucide-trophy', to: '/truemains', active: path === '/truemains' },
        { label: 'Favorites', icon: 'i-lucide-star', to: '/truemains/favorites', active: path === '/truemains/favorites' },
      ],
    },
    {
      label: 'Companion',
      items: [
        { label: 'Desktop app', icon: 'i-lucide-monitor-down', to: '/download', active: path === '/download', badge: 'Beta' },
      ],
    },
  ]
}

export interface Crumb {
  label: string
  to?: string
}

const SECTION: Record<string, { label: string, icon: string }> = {
  '': { label: 'Overview', icon: 'i-lucide-layout-dashboard' },
  'champions': { label: 'Champions', icon: 'i-lucide-swords' },
  'matchup': { label: 'Matchup', icon: 'i-lucide-wand-sparkles' },
  'truemains': { label: 'Truemains', icon: 'i-lucide-trophy' },
  'players': { label: 'Players', icon: 'i-lucide-user-round' },
  'download': { label: 'Desktop app', icon: 'i-lucide-monitor-down' },
  'builder': { label: 'Builder', icon: 'i-lucide-hammer' },
  'about': { label: 'About', icon: 'i-lucide-info' },
  'privacy': { label: 'Privacy', icon: 'i-lucide-shield' },
  'terms': { label: 'Terms', icon: 'i-lucide-scroll-text' },
}

function titleCase(segment: string): string {
  return decodeURIComponent(segment)
    .replace(/[-_]+/g, ' ')
    .replace(/\b\w/g, c => c.toUpperCase())
}

export interface Trail {
  icon: string
  crumbs: Crumb[]
}

/**
 * The context bar's trail. `nameOf` turns a dynamic segment into what the page
 * calls it (a champion's name from its slug, a Riot ID from a name-tag), given
 * the segment before it; it returns null to fall back to the segment itself.
 * Only the section and its direct child link: deeper parents (a player's
 * `/champions`) are not pages of their own.
 */
export function routeTrail(
  path: string,
  nameOf: (segment: string, parent: string) => string | null,
): Trail {
  const segments = path.split('/').filter(Boolean)
  const head = segments[0] ?? ''
  const section = SECTION[head] ?? { label: titleCase(head), icon: 'i-lucide-panel-top' }
  const crumbs: Crumb[] = [{ label: section.label, to: `/${head}` }]

  let at = `/${head}`
  segments.slice(1).forEach((segment, index) => {
    at += `/${segment}`
    const parent = segments[index] ?? ''
    const label = segment === 'tierlist'
      ? 'Tier List'
      : segment === 'favorites' && parent === 'truemains'
        ? 'Favorites'
        : nameOf(segment, parent) ?? titleCase(segment)
    crumbs.push(index === 0 ? { label, to: at } : { label })
  })

  // The last crumb is where the reader is: not a link.
  const last = crumbs.at(-1)
  if (last) delete last.to
  return { icon: section.icon, crumbs }
}

/** `16.22.1` → `16.22`: the patch as players name it. */
export function shortPatch(version: string | null | undefined): string | null {
  if (!version) return null
  const [major, minor] = version.split('.')
  return major && minor ? `${major}.${minor}` : version
}

// Nuxt registers every file under `components/` as a component; a default
// export keeps that registration well-typed. Import the named helpers.
export default navGroups
