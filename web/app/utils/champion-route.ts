import type { RouteLocationNormalized } from 'vue-router'

/**
 * Route guard shared by the two champion pages (#1124): resolve the `slug`
 * param, 404 what names no champion, and 301 anything that isn't the canonical
 * segment.
 *
 * A **middleware** rather than a check in `setup`, because setup does not re-run
 * when only a route param changes — champion → champion reuses the component —
 * so a guard written in setup fires on a full page load and silently stops
 * firing on every client-side navigation after it.
 *
 * Takes a `basePath` builder because the two routes differ only in their prefix
 * (`/champions/{slug}` vs `/truemains/{nameTag}/champions/{slug}`), and the
 * player-scoped one has to keep its `nameTag` — rebuilding it from `to.params`
 * at each call site is what keeps that from being reconstructed wrongly here.
 */
export function championRouteGuard(
  to: RouteLocationNormalized,
  basePath: (segment: string) => string,
) {
  const { routeAction } = useChampionSlugs()
  const action = routeAction(String(to.params.slug ?? ''))

  switch (action.type) {
    // Not "no data for this champion" — no such champion. The empty-build state
    // would tell a crawler the URL is real and merely thin, which is the wrong
    // answer for a typo'd or long-dead URL.
    case 'notFound':
      return abortNavigation(createError({ statusCode: 404, statusMessage: 'Champion not found' }))

    // The slug map never loaded, so a real champion and a typo are
    // indistinguishable. Answer "come back later", not "this never existed".
    case 'unavailable':
      return abortNavigation(createError({
        statusCode: 503,
        statusMessage: 'Champion directory temporarily unavailable',
      }))

    // Legacy `/champions/103` and mis-cased `/champions/Ahri` both land here.
    // Permanent, not a rewrite: the two URLs render the same page, so the
    // ranking signal has to consolidate on one instead of splitting. Query and
    // hash ride along — the patch / lane / rank / matchup filters are what make
    // a shared champion link worth sharing.
    case 'redirect':
      return navigateTo(
        { path: basePath(action.segment), query: to.query, hash: to.hash },
        { redirectCode: 301, replace: true },
      )
  }
}
