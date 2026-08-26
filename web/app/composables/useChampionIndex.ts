import type { ChampionIndexResponse } from '~~/shared/types/champion-index'

/**
 * The server-rendered champion link graph (#1209).
 *
 * Both fetches are **SSR-enabled**, which is the entire point: the anchors have
 * to exist in the HTML a crawler receives, and every other champion fetch on
 * these pages is `server: false`.
 *
 * Not a #149 regression, and the distinction is the same one #1123 turns on:
 * #149 was a *client-only* fetch racing SSR and winning, so the server rendered
 * content the client's first render didn't have. These are SSR-enabled and
 * travel in the Nuxt payload, so hydration reads the same object the server
 * rendered from — the two agree by construction. Every interactive panel on
 * these pages stays client-only exactly as it was.
 *
 * Callers await these **server-side only** (`if (import.meta.server) await …`),
 * the idiom the champion page's own SSR fetches already use: the app has no
 * Suspense fallback on `<NuxtPage>`, so awaiting on the client would freeze the
 * outgoing page on every client-side navigation, purely for a supplementary
 * block. The block is absent from the first client render after such a
 * navigation and appears when the fetch lands, which is correct — nothing was
 * server-rendered for it to disagree with.
 *
 * One composable rather than an inline `useAsyncData` per page so the shared
 * keys carry identical options — Nuxt warns (and serves one page's payload to
 * another) when the same key is used with diverging ones.
 */

/** Shared empty value, so every caller's `data` is non-null before the fetch lands. */
function emptyIndex(): ChampionIndexResponse {
  return { patch: null, champions: [], tiers: [] }
}

/**
 * Every live champion, A→Z. Filter-independent, so the key is a constant and
 * the payload is shared by every page that renders the index — `/champions`
 * and a champion page hand each other the same entry.
 */
export function useChampionIndexAll() {
  return useAsyncData<ChampionIndexResponse>(
    'champion-index-all',
    () => $fetch<ChampionIndexResponse>('/api/champion-index', { query: { view: 'all' } }),
    { default: emptyIndex },
  )
}

/**
 * The tier list as named links, scoped to the **URL** filters.
 *
 * Keyed on the URL rather than on the page's reconciled `selectedPatch`: that
 * one resolves to the API's own patch once the client-only tier-list fetch
 * lands, which would change the key after hydration and cost a second
 * round-trip (plus a visible re-render) on every load. The URL filters are
 * identical on the server and at hydration, and the endpoint resolves the same
 * defaults the tier list does, so both describe the same slice.
 */
export function useChampionIndexTiers(options: {
  patch?: MaybeRefOrGetter<string | null | undefined>
  position?: MaybeRefOrGetter<string | null | undefined>
  eloBracket?: MaybeRefOrGetter<string | null | undefined>
  /** Caps the total entries across tiers — the homepage block wants a dozen. */
  limit?: number
} = {}) {
  const patch = computed(() => toValue(options.patch) || '')
  const position = computed(() => toValue(options.position) || '')
  const eloBracket = computed(() => toValue(options.eloBracket) || '')
  const limit = options.limit

  return useAsyncData<ChampionIndexResponse>(
    () => ['champion-index-tiers', patch.value, position.value, eloBracket.value, limit ?? ''].join('-'),
    () => $fetch<ChampionIndexResponse>('/api/champion-index', {
      query: {
        view: 'tiers',
        patch: patch.value || undefined,
        position: position.value || undefined,
        eloBracket: eloBracket.value || undefined,
        limit,
      },
    }),
    {
      watch: [patch, position, eloBracket],
      default: emptyIndex,
    },
  )
}
