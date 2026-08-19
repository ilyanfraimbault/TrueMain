import type { ChampionSlugMap } from '~~/shared/types/static-data'
import {
  buildChampionIdBySlug,
  championPath,
  championRouteAction,
  resolveChampionParam,
  truemainChampionPath,
} from '~~/shared/utils/champion-slug'

/** Shared between the plugin that fills the state and the composable that reads it. */
export const CHAMPION_SLUGS_STATE_KEY = 'champion-slugs'

/**
 * Champion URL slugs (#1124), from the app-wide state `plugins/champion-slugs.ts`
 * fills before the first render.
 *
 * Reading is synchronous everywhere — server render, hydration and every later
 * client-side navigation — which is the whole reason the map is app state
 * instead of per-page async data: a link that resolved asynchronously would
 * render numeric on the server and slugged on the client.
 *
 * Never fetches. A component that calls this gets whatever the plugin loaded,
 * and the builders degrade to numeric ids when that is nothing.
 */
export function useChampionSlugs() {
  const slugs = useState<ChampionSlugMap>(CHAMPION_SLUGS_STATE_KEY, () => ({}))
  const idBySlug = computed(() => buildChampionIdBySlug(slugs.value))

  return {
    slugs,
    /** `/champions/{slug}` for the global build page. */
    pathFor: (championId: number) => championPath(championId, slugs.value),
    /** `/truemains/{nameTag}/champions/{slug}` for the player-scoped one. */
    truemainPathFor: (nameTag: string, championId: number) =>
      truemainChampionPath(nameTag, championId, slugs.value),
    /** Reads a route param back to a champion + the segment it belongs under. */
    resolveParam: (segment: string) => resolveChampionParam(segment, slugs.value, idBySlug.value),
    /** What the route guard should do with a param: render / redirect / 404 / 503. */
    routeAction: (segment: string) => championRouteAction(segment, slugs.value, idBySlug.value),
  }
}
