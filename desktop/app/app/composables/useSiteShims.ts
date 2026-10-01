import type { MatchDetailResponse } from '#shared/types/match-detail'
import type { ChampionStaticListItem } from '#shared/types/static-data'

/**
 * The app's side of the composables the twinned site components call, so the
 * components themselves stay verbatim copies. Each answers the same question
 * the site's does, for an app with no image server and no player pages.
 */

/**
 * The site's `useChampionSlugs`, answered with the app's routes: a champion
 * opens its builds page here. The app has no player-scoped champion page, so a
 * true main's champion opens the champion's own page.
 */
export function useChampionSlugs() {
  const pathFor = (championId: number) => `/champions/${championId}`
  return {
    pathFor,
    truemainPathFor: (_nameTag: string, championId: number) => pathFor(championId),
  }
}

/** The site routes icons through its image server; the app uses them as given. */
export function useCanonicalIcon() {
  return (src: string | null | undefined) => src ?? undefined
}

/**
 * Every champion as the site's static list item, keyed by id — the site's
 * `useChampionsById` over the static list, read from Data Dragon at once.
 */
export function useStaticChampionsById() {
  const { champions, portraitOf } = useChampionStatics()
  return computed(() => new Map<number, ChampionStaticListItem>(
    [...champions.value.values()].map(champion => [champion.id, {
      championId: champion.id,
      name: champion.name,
      iconUrl: portraitOf(champion.id) ?? '',
    }]),
  ))
}

/**
 * One of the player's own games in the site's match-detail shape, read from
 * their client: the shell reads the game's scoreboard and timeline
 * (`player_game`). The dashboard's counterpart of the site's `useMatchDetail`,
 * which reads TrueMain's API and serves the shared pages. The client only opens
 * the logged-in player's games. In `npm run dev` the dev fixtures stand in.
 */
const matchDetails = new Map<string, MatchDetailResponse | null>()

async function readMatchDetail(matchId: string): Promise<MatchDetailResponse | null> {
  if (insideTauri()) {
    const { invoke } = await import('@tauri-apps/api/core')
    return await invoke<MatchDetailResponse>('player_game', { gameId: Number(matchId) })
  }
  if (import.meta.dev) {
    const fixtures = await import('~/fixtures/match-details.json')
    return (fixtures.default as unknown as Record<string, MatchDetailResponse>)[matchId] ?? null
  }
  return null
}

export function usePlayerGameDetail(matchId: MaybeRefOrGetter<string>) {
  const data = ref<MatchDetailResponse | null>(null)
  const isLoading = ref(true)
  const notFound = ref(false)

  watch(() => toValue(matchId), async (id) => {
    isLoading.value = true
    try {
      if (!matchDetails.has(id)) matchDetails.set(id, await readMatchDetail(id))
      data.value = matchDetails.get(id) ?? null
    }
    catch {
      data.value = null
    }
    notFound.value = !data.value
    isLoading.value = false
  }, { immediate: true })

  return { data, isLoading, notFound }
}
