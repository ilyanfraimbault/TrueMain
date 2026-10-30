import type { TruemainIdentity } from '~~/shared/types/truemain-identity'

/**
 * What a crawler is told about a truemain page (#2335, #2336): its status code
 * and its canonical URL, both from `/api/truemain-identity/{nameTag}` — the one
 * server-side fact the page has, its content being client-only (#862).
 *
 * - **Unknown account → `404`** on the server render, instead of a `200` page
 *   that draws "not found" in the browser (a soft 404 to Google). The page
 *   itself is unchanged: its client fetch still renders the not-found state.
 * - **Canonical → the stored Riot ID's slug**, through `pathFor`, replacing
 *   `nuxt-seo-utils`' lowercased one. While the lookup has no answer (in flight
 *   on a client navigation, or the API down) the route's own slug stands in:
 *   never lowercased, never a guess at another account.
 *
 * Public and keyed on the slug alone, so it may ride the SSR payload — unlike
 * the profile fetches, which stay client-only. Returns the `useAsyncData`
 * promise for the page to await with its other fetches (#1689).
 */
export function useTruemainPageIndexing(
  nameTag: ComputedRef<string>,
  pathFor: (canonicalNameTag: string) => string,
) {
  const apiFetch = useApiFetch()
  const event = useRequestEvent()

  const lookup = useAsyncData<TruemainIdentity | null>(
    () => `truemain-identity-${nameTag.value}`,
    (_nuxtApp, { signal }) => nameTag.value
      ? apiFetch<TruemainIdentity>(`/truemain-identity/${encodeURIComponent(nameTag.value)}`, { signal })
      : Promise.resolve(null),
    { watch: [nameTag], default: () => null },
  )

  // Only a definite 404 is "unknown": a 502 (API down) or a 429 is no verdict.
  const unknown = computed(() => lookup.error.value?.statusCode === 404)

  const canonicalPath = computed(() => pathFor(lookup.data.value?.nameTag ?? nameTag.value))
  const canonicalUrl = withSiteUrl(canonicalPath)

  useHead({
    link: () => unknown.value ? [] : [{ rel: 'canonical', href: canonicalUrl.value }],
  })
  useSeoMeta({ ogUrl: () => unknown.value ? undefined : canonicalUrl.value })

  return lookup.then(() => {
    if (import.meta.server && event && unknown.value) {
      setResponseStatus(event, 404)
    }
  })
}
