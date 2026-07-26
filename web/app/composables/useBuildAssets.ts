import type { RuneTreeResponse, StaticItemData, StaticPerkData, StaticPerkStyleData, StaticSummonerSpellData } from '~~/shared/types/static-data'

/**
 * Turns build ids (keystone / secondary style / first item) into the icon
 * objects the `GameTooltip*` components render, given an already-fetched rune
 * tree + item map. The shared primitive: {@link useBuildAssets} uses it after
 * fetching, and the components that receive those maps as props (LeaderboardRow,
 * the homepage teaser) use it directly — so every call site resolves ids the
 * exact same way without copy-pasting the three lookups.
 */
export function useBuildResolvers(
  runeTree: MaybeRefOrGetter<RuneTreeResponse | null | undefined>,
  itemsMap: MaybeRefOrGetter<Record<number, StaticItemData> | undefined>,
) {
  function perk(id: number | null | undefined): StaticPerkData | null {
    return id != null ? toValue(runeTree)?.perks?.[id] ?? null : null
  }
  function perkStyle(id: number | null | undefined): StaticPerkStyleData | null {
    return id != null ? toValue(runeTree)?.perkStyles?.[id] ?? null : null
  }
  function item(id: number | null | undefined): StaticItemData | null {
    return id != null ? toValue(itemsMap)?.[id] ?? null : null
  }
  return { perk, perkStyle, item }
}

interface StaticFetchOptions {
  /**
   * Defer the first fetch until the caller triggers `execute` (used by the
   * champion list, which waits for the patch to resolve so it doesn't issue
   * a redundant `latest` round trip and immediately refetch under the
   * resolved patch key).
   */
  immediate?: boolean
  /**
   * Key segment used while the patch is still unresolved. Defaults to
   * `latest` — pair a different segment with `immediate: false` when the
   * unresolved key must never collide with a real `latest` payload.
   */
  unresolvedKeySegment?: string
  /**
   * Run the fetch during SSR so the payload is present at hydration instead of
   * only after a post-hydration client round trip (issue #818 — the static
   * maps were the last thing gating the build breakdown behind
   * SSR → hydration → client fetch). Defaults to `false` to preserve the
   * historical client-only behaviour; opt in per lookup where the SSR payload
   * cost is worth the earlier paint. See the size trade-off below the
   * `useStaticItems` factory. The endpoints are 1h `defineCachedFunction`s, so
   * an SSR fetch is a Nitro cache hit rather than an extra upstream round trip.
   */
  server?: boolean
}

/**
 * Shared factory for the patch-keyed static lookups below. Each fetch is
 * TTL-cached (see static-cache.ts) under a canonical `{prefix}-{patch}` key, so
 * every page that needs the same static payload dedupes against the same Nuxt
 * cache entry across navigations. The key is captured before the network round
 * trip so `markStaticFetched` can't stamp a different key if the patch changes
 * mid-flight.
 *
 * `options.server` decides whether the fetch also runs during SSR. When it
 * does, `useLazyAsyncData` serialises the result into the Nuxt payload and the
 * client hydrates from it (no post-hydration round trip); the
 * `getStaticCachedData` server guard still forces the handler to run on the
 * server rather than short-circuiting on a cache value, so the SSR render and
 * the client's initial hydration always agree on the payload. Subsequent
 * client-side remounts keep deduping through the TTL cache exactly as before.
 */
function useStaticFetch<T>(
  keyPrefix: string,
  endpoint: string,
  patch: MaybeRefOrGetter<string | null | undefined>,
  options: StaticFetchOptions = {},
) {
  const nuxtApp = useNuxtApp()
  const patchRef = computed(() => toValue(patch) || null)
  const keyRef = computed(() => `${keyPrefix}-${patchRef.value || (options.unresolvedKeySegment ?? 'latest')}`)

  return useLazyAsyncData<T>(
    () => keyRef.value,
    async () => {
      const key = keyRef.value
      const data = await $fetch<T>(endpoint, {
        query: patchRef.value ? { patch: patchRef.value } : {},
      })
      markStaticFetched(key, nuxtApp)
      return data
    },
    {
      watch: [patchRef],
      immediate: options.immediate ?? true,
      server: options.server ?? false,
      getCachedData: key => getStaticCachedData(key, nuxtApp),
    },
  )
}

/**
 * Static rune tree for a patch, keyed `rune-tree-{patch}`. SSR-rendered by
 * default: the tree is small and feeds the keystone / secondary-rune icons in
 * the build breakdown, so having it in the payload at hydration removes one of
 * the client round trips gating that content (issue #818). Callers can still
 * override via `options` — the champion detail page defers it with
 * `{ immediate: false }` to avoid the unresolved-patch double fetch (issue #817).
 */
export function useStaticRuneTree(
  patch: MaybeRefOrGetter<string | null | undefined>,
  options: StaticFetchOptions = {},
) {
  return useStaticFetch<RuneTreeResponse>('rune-tree', '/api/static/rune-tree', patch, { server: true, ...options })
}

/**
 * Static item map for a patch, keyed `static-items-{patch}`.
 *
 * Client-only by default (`server: false`), deliberately unlike the rune /
 * summoner maps: this payload carries every item's full `description` /
 * `plaintext` (for the hover tooltips), which makes it ~373 KiB (the figure
 * measured in #818) — the rune tree and summoner map are a fraction of that.
 * SSR-ing it would inline that blob into the HTML of every page that renders a
 * build,
 * including the homepage teaser and the truemains leaderboard (both via
 * `useBuildAssets`), for a below-the-fold benefit there. Callers where the
 * earlier paint is worth the payload can opt in with `{ server: true }`.
 */
export function useStaticItems(
  patch: MaybeRefOrGetter<string | null | undefined>,
  options: StaticFetchOptions = {},
) {
  return useStaticFetch<Record<number, StaticItemData>>('static-items', '/api/static/items', patch, options)
}

/**
 * Static summoner-spell map for a patch, keyed `static-summoners-{patch}`.
 * SSR-rendered by default: only ~18 entries, so the payload cost is negligible
 * and the summoner-spell icons render straight from the hydration payload
 * (issue #818). Callers can override via `options` — the champion detail page
 * defers it with `{ immediate: false }` (issue #817).
 */
export function useStaticSummonerSpells(
  patch: MaybeRefOrGetter<string | null | undefined>,
  options: StaticFetchOptions = {},
) {
  return useStaticFetch<Record<number, StaticSummonerSpellData>>('static-summoners', '/api/static/summoner-spells', patch, { server: true, ...options })
}

/**
 * Fetches the static rune tree + item map for a patch and exposes resolvers
 * that turn build ids (keystone / secondary style / first item) into the icon
 * objects the `GameTooltip*` components render. Client-only and TTL-cached the
 * same way as the other static lookups, so it piggybacks on whatever the
 * champion list already warmed.
 *
 * Shared by the truemains leaderboard and the homepage teaser, which both want
 * to show each player's main-champion build.
 */
export function useBuildAssets(patch: MaybeRefOrGetter<string | null | undefined>) {
  const { data: runeTreeData } = useStaticRuneTree(patch)
  const { data: itemsData } = useStaticItems(patch)

  const runeTree = computed(() => runeTreeData.value ?? null)
  const itemsMap = computed(() => itemsData.value ?? {})

  const { perk, perkStyle, item } = useBuildResolvers(runeTree, itemsMap)

  return { runeTree, itemsMap, perk, perkStyle, item }
}
