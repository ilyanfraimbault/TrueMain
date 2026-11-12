/**
 * Variant E ("Launcher") — the destinations the rail and the phone tab bar both
 * draw, with the same active rules as `AppHeader`: the tier list does not light
 * Champions, and a player profile lights nothing (only the leaderboard itself
 * lights Truemains).
 */
export interface LauncherNavItem {
  key: string
  label: string
  icon: string
  to: string
  badge?: string
}

export const LAUNCHER_PRIMARY: LauncherNavItem[] = [
  { key: 'home', label: 'Home', icon: 'i-lucide-house', to: '/' },
  { key: 'champions', label: 'Champions', icon: 'i-lucide-swords', to: '/champions' },
  { key: 'tierlist', label: 'Tier List', icon: 'i-lucide-trending-up', to: '/champions/tierlist' },
  { key: 'matchup', label: 'Matchup', icon: 'i-lucide-wand-sparkles', to: '/matchup' },
  { key: 'truemains', label: 'Truemains', icon: 'i-lucide-trophy', to: '/truemains' },
  { key: 'favorites', label: 'Favorites', icon: 'i-lucide-star', to: '/truemains/favorites' },
]

export const LAUNCHER_DESKTOP: LauncherNavItem = {
  key: 'download',
  label: 'Desktop app',
  icon: 'i-lucide-monitor-down',
  to: '/download',
  badge: 'Beta',
}

function under(path: string, prefix: string): boolean {
  return path === prefix || path.startsWith(`${prefix}/`)
}

/** Which nav entry the path lights, or null (a player profile, /about…). */
export function activeNavKey(path: string): string | null {
  if (path === '/') return 'home'
  if (under(path, '/champions/tierlist')) return 'tierlist'
  if (under(path, '/champions')) return 'champions'
  if (under(path, '/matchup')) return 'matchup'
  if (path === '/truemains') return 'truemains'
  if (path === '/truemains/favorites') return 'favorites'
  if (path === '/download') return 'download'
  return null
}

// Nuxt registers every file under `components/` as a component; a default
// export keeps that registration well-typed. Import the named members.
export default activeNavKey
