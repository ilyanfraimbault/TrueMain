import type { ProfileResponse } from '#shared/types/profile'
import type { FavoriteTruemain } from '#common/utils/favorites'

/** A favorite's profile: `undefined` while unasked, `null` when it could not be read. */
type Profile = ProfileResponse | null

const inFlight = new Set<string>()

/**
 * The true mains the player follows (the site's favorites, #531) who main a
 * champion — what the build view lists first (#1733), since the reason to
 * follow a main is to copy what they run. Whether a favorite mains the
 * champion is read from their profile's mains, asked once per launch for each
 * favorite; one whose profile cannot be read is left out rather than guessed.
 */
export function useFavoriteMains(championId: MaybeRefOrGetter<number>) {
  const { favorites } = useFavoriteTruemains()
  const profiles = useState<Record<string, Profile>>('favorite-profiles', () => ({}))

  async function ask(nameTag: string) {
    if (nameTag in profiles.value || inFlight.has(nameTag)) return
    inFlight.add(nameTag)
    let answer: Profile = null
    try {
      answer = await apiGet<ProfileResponse>(`/truemains/${encodeURIComponent(nameTag)}/profile`)
    }
    catch {
      // Not tracked any more, or TrueMain unreachable: the list goes without them.
    }
    finally {
      inFlight.delete(nameTag)
    }
    profiles.value = { ...profiles.value, [nameTag]: answer }
  }

  watch(favorites, (list) => {
    for (const favorite of list) void ask(favorite.nameTag)
  }, { immediate: true })

  return computed<{ favorite: FavoriteTruemain, profile: ProfileResponse }[]>(() => {
    const id = toValue(championId)
    return favorites.value.flatMap((favorite) => {
      const profile = profiles.value[favorite.nameTag]
      return profile?.mains.some(main => main.championId === id) ? [{ favorite, profile }] : []
    })
  })
}
