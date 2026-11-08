import type { PersonaResponse } from '#shared/types/players'
import { parseRouteParam } from '#common/utils/route-params'
import { mergedPlayerPath } from '#common/utils/player-redirect'
import { isNotFoundError } from '#common/utils/remote-read'

/**
 * The known player the route names (#2016), read once for both apps' pages. The
 * site awaits `ready` so its head and its status are the player's in the first
 * HTML; the app does not, so the tab changes on click and the page's skeleton
 * stands until the player is in.
 */
export function useKnownPlayer() {
  const route = useRoute()
  const slug = computed(() => parseRouteParam(route.params.slug).toLowerCase())

  const apiFetch = useApiFetch()
  const read = useAsyncData<PersonaResponse | null>(
    () => `player-${slug.value}`,
    async (_nuxtApp, { signal }) => {
      try {
        const response = await apiFetch<PersonaResponse | null>(
          `/players/${encodeURIComponent(slug.value)}`,
          { signal },
        )
        return response && typeof response === 'object' && response.displayName ? response : null
      }
      catch (err) {
        // Only a 404 is "unknown". An outage stays an error, shown as one — and
        // never answered to a crawler with the 404 an unknown player gets (#2476).
        if (isNotFoundError(err)) return null
        throw err
      }
    },
    { watch: [slug] },
  )

  const notFound = computed(() => read.status.value === 'success' && !read.data.value)

  // A known player an operator merged into another one (#2253) answers under the
  // kept player's slug: send the visitor there. The site answers a crawler's first
  // request with a permanent redirect of its own, before anything renders.
  const mergedTarget = computed(() => mergedPlayerPath(slug.value, read.data.value))
  watch(mergedTarget, (target) => {
    if (target) {
      navigateTo(target, { replace: true })
    }
  }, { immediate: true })

  return { slug, persona: read.data, status: read.status, error: read.error, notFound, mergedTarget, ready: read }
}
