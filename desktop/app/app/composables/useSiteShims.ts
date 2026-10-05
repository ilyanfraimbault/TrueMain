import type { MatchDetailResponse } from '#shared/types/match-detail'
import type { ChampionStaticListItem } from '#shared/types/static-data'
import type { WinProbabilityTimeline } from '#shared/types/win-probability'

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

/** TrueMain's copies already read: an ingested game never changes. */
const matchDetails = new Map<string, MatchDetailResponse>()

async function readTruemainDetail(nameTag: string, platformId: string | null, matchId: string): Promise<MatchDetailResponse | null> {
  if (!nameTag || !platformId) return null
  try {
    return await apiGet<MatchDetailResponse>(`/truemains/${encodeURIComponent(nameTag)}/matches/${platformId}_${matchId}`)
  }
  catch {
    return null
  }
}

async function readClientDetail(matchId: string): Promise<MatchDetailResponse | null> {
  if (insideTauri()) {
    const { invoke } = await import('@tauri-apps/api/core')
    return await invoke<MatchDetailResponse>('player_game', { gameId: Number(matchId) })
  }
  if (import.meta.dev) {
    const fixtures = await import('~/fixtures/match-details.json')
    const detail = (fixtures.default as unknown as Record<string, MatchDetailResponse>)[matchId]
    if (!detail) return null
    // The shared model's fixture game stands in for every game's timeline, so the curve shows in dev.
    const { default: winProbability } = await import('#shared/fixtures/win-probability-timeline.json')
    return { ...detail, winProbabilityTimeline: winProbability.timeline as WinProbabilityTimeline }
  }
  return null
}

/**
 * One of the player's own games in the site's match-detail shape — the
 * dashboard's counterpart of the site's `useMatchDetail`. TrueMain's copy of the
 * game comes first: it alone has the build and skill orders, since the
 * client's timeline carries no purchases and no skill points. A game TrueMain
 * has not ingested falls back to the client's read (`player_game`): the
 * scoreboard, the roles and the lane at fifteen, without those two. In
 * `npm run dev` the dev fixtures stand in for the client.
 *
 * The win-probability curve (#1911) is TrueMain's own (`winProbability`) when
 * its copy has one; a copy without — a game ingested before the curve existed —
 * borrows the client's timeline (`winProbabilityTimeline`) once the panel is
 * shown, so the Timeline tab appears in either case when the game allows it.
 */
export function usePlayerGameDetail(nameTag: MaybeRefOrGetter<string>, matchId: MaybeRefOrGetter<string>) {
  const { record } = usePlayerRecord()
  const data = ref<MatchDetailResponse | null>(null)
  const isLoading = ref(true)
  const notFound = ref(false)

  watch(() => toValue(matchId), async (id) => {
    isLoading.value = true
    try {
      const known = matchDetails.get(id)
      const truemain = known ?? await readTruemainDetail(toValue(nameTag), record.value?.platformId ?? null, id)
      if (truemain) matchDetails.set(id, truemain)
      // The client's fallback is cached by the shell; TrueMain is asked again
      // next time, since it may have ingested the game since.
      data.value = truemain ?? await readClientDetail(id)
    }
    catch {
      data.value = null
    }
    notFound.value = !data.value
    isLoading.value = false
    if (data.value && !data.value.winProbability && !data.value.winProbabilityTimeline && insideTauri()) {
      const client = await readClientDetail(id).catch(() => null)
      if (client?.winProbabilityTimeline && toValue(matchId) === id && data.value) {
        data.value = { ...data.value, winProbabilityTimeline: client.winProbabilityTimeline }
      }
    }
  }, { immediate: true })

  return { data, isLoading, notFound }
}
