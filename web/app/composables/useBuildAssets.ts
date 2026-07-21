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
}

/**
 * Shared factory for the patch-keyed static lookups below. Each fetch is
 * client-only and TTL-cached (see static-cache.ts) under a canonical
 * `{prefix}-{patch}` key, so every page that needs the same static payload
 * dedupes against the same Nuxt cache entry across navigations. The key is
 * captured before the network round trip so `markStaticFetched` can't stamp
 * a different key if the patch changes mid-flight.
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
      server: false,
      getCachedData: key => getStaticCachedData(key, nuxtApp),
    },
  )
}

/** Static rune tree for a patch, keyed `rune-tree-{patch}`. */
export function useStaticRuneTree(
  patch: MaybeRefOrGetter<string | null | undefined>,
  options: StaticFetchOptions = {},
) {
  return useStaticFetch<RuneTreeResponse>('rune-tree', '/api/static/rune-tree', patch, options)
}

/** Static item map for a patch, keyed `static-items-{patch}`. */
export function useStaticItems(
  patch: MaybeRefOrGetter<string | null | undefined>,
  options: StaticFetchOptions = {},
) {
  return useStaticFetch<Record<number, StaticItemData>>('static-items', '/api/static/items', patch, options)
}

/** Static summoner-spell map for a patch, keyed `static-summoners-{patch}`. */
export function useStaticSummonerSpells(
  patch: MaybeRefOrGetter<string | null | undefined>,
  options: StaticFetchOptions = {},
) {
  return useStaticFetch<Record<number, StaticSummonerSpellData>>('static-summoners', '/api/static/summoner-spells', patch, options)
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
