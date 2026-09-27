import type { TruemainIdentity } from '~/types/truemains'
import { nameTagOf } from '~/types/truemains'

const STORAGE_KEY = 'truemain:favorites'

/**
 * Starred true mains, kept on this machine.
 *
 * The site keeps its favorites in the browser's own storage, which the app
 * cannot read, so the two lists are separate — the same gesture, not the same
 * list. Stored as the identity the leaderboard answered with, so the page can
 * draw a favorite without asking the API for it again.
 */
export function useFavorites() {
  const favorites = useState<TruemainIdentity[]>('favorites', () => read())

  function read(): TruemainIdentity[] {
    try {
      const raw = localStorage.getItem(STORAGE_KEY)
      return raw ? JSON.parse(raw) as TruemainIdentity[] : []
    }
    catch {
      return []
    }
  }

  function write(next: TruemainIdentity[]) {
    favorites.value = next
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(next))
    }
    catch {
      // Storage refused (private mode, quota): the star still works for the session.
    }
  }

  const isFavorite = (identity: TruemainIdentity) =>
    favorites.value.some(entry => nameTagOf(entry) === nameTagOf(identity))

  function toggle(identity: TruemainIdentity) {
    write(isFavorite(identity)
      ? favorites.value.filter(entry => nameTagOf(entry) !== nameTagOf(identity))
      : [...favorites.value, identity])
  }

  return { favorites, isFavorite, toggle }
}
