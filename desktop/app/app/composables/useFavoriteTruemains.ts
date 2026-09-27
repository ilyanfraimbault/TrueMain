import type { RegionSlug } from '~~/shared/types/leaderboard'

const STORAGE_KEY = 'truemain:favorites:v1'
/** The site's own ceiling (`FAVORITES_LIMIT` in `web/app/utils/favorites.ts`). */
export const FAVORITES_LIMIT = 30

/** A followed true main — the fields the site's store keeps. */
export interface FavoriteTruemain {
  gameName: string
  tagLine: string | null
  region: RegionSlug | null
  profileIconId: number | null
}

/** The site's profile slug: `{gameName}-{tagLine}`, or the name alone when untagged. */
export function favoriteNameTag(gameName: string, tagLine: string | null | undefined): string {
  return tagLine ? `${gameName}-${tagLine}` : gameName
}

/**
 * Followed true mains, kept on this machine — the same list, gesture and
 * ceiling as the site's favorites (`useFavoriteTruemains`), in the app's own
 * storage: the site's lives in the browser, which the app cannot read. Named
 * like the site's composable so the twinned `FavoriteToggle` reads it the same way.
 */
export function useFavoriteTruemains() {
  const favorites = useState<FavoriteTruemain[]>('favorites', () => read())

  function read(): FavoriteTruemain[] {
    try {
      const raw = localStorage.getItem(STORAGE_KEY)
      return raw ? JSON.parse(raw) as FavoriteTruemain[] : []
    }
    catch {
      return []
    }
  }

  function write(next: FavoriteTruemain[]) {
    favorites.value = next
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(next))
    }
    catch {
      // Storage refused (private mode, quota): the star still works for the session.
    }
  }

  const keyOf = (entry: FavoriteTruemain) => favoriteNameTag(entry.gameName, entry.tagLine).toLowerCase()
  const isFavorite = (nameTag: string) => favorites.value.some(entry => keyOf(entry) === nameTag.toLowerCase())
  const atLimit = computed(() => favorites.value.length >= FAVORITES_LIMIT)

  function toggle(entry: FavoriteTruemain) {
    const key = keyOf(entry)
    if (favorites.value.some(existing => keyOf(existing) === key)) {
      write(favorites.value.filter(existing => keyOf(existing) !== key))
      return
    }
    if (!atLimit.value) write([...favorites.value, entry])
  }

  return { favorites, isFavorite, toggle, atLimit }
}
