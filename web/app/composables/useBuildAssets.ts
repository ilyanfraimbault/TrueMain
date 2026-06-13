import type { RuneTreeResponse, StaticItemData, StaticPerkData, StaticPerkStyleData } from '~~/shared/types/static-data'

/**
 * Turns build ids (keystone / secondary style / first item) into the icon
 * objects the `GameTooltip*` components render, given an already-fetched rune
 * tree + item map. Split out from {@link useBuildAssets} so components that
 * receive the maps as props (LeaderboardRow, the homepage teaser) resolve them
 * the exact same way the fetching composable does, without copy-pasting the
 * three lookups.
 */
export function useBuildResolvers(
  runeTree: MaybeRefOrGetter<RuneTreeResponse | null | undefined>,
  itemsMap: MaybeRefOrGetter<Record<number, StaticItemData> | undefined>,
) {
  function perk(id: number | null | undefined): StaticPerkData | null {
    return id ? toValue(runeTree)?.perks?.[id] ?? null : null
  }
  function perkStyle(id: number | null | undefined): StaticPerkStyleData | null {
    return id ? toValue(runeTree)?.perkStyles?.[id] ?? null : null
  }
  function item(id: number | null | undefined): StaticItemData | null {
    return id ? toValue(itemsMap)?.[id] ?? null : null
  }
  return { perk, perkStyle, item }
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
  const nuxtApp = useNuxtApp()
  const patchRef = computed(() => toValue(patch) || null)

  // Single source for each cache key — reused as the useAsyncData key and by
  // markStaticFetched, so the two can't drift out of sync (a mismatch would
  // make getStaticCachedData miss the stored entry every time).
  const runeTreeKey = computed(() => `rune-tree-${patchRef.value || 'latest'}`)
  const itemsKey = computed(() => `static-items-${patchRef.value || 'latest'}`)

  const { data: runeTree } = useLazyAsyncData<RuneTreeResponse | null>(
    () => runeTreeKey.value,
    async () => {
      const data = await $fetch<RuneTreeResponse>('/api/static/rune-tree', {
        query: patchRef.value ? { patch: patchRef.value } : {},
      })
      markStaticFetched(runeTreeKey.value, nuxtApp)
      return data
    },
    { watch: [patchRef], server: false, default: () => null, getCachedData: key => getStaticCachedData(key, nuxtApp) },
  )

  const { data: itemsMap } = useLazyAsyncData<Record<number, StaticItemData>>(
    () => itemsKey.value,
    async () => {
      const data = await $fetch<Record<number, StaticItemData>>('/api/static/items', {
        query: patchRef.value ? { patch: patchRef.value } : {},
      })
      markStaticFetched(itemsKey.value, nuxtApp)
      return data
    },
    { watch: [patchRef], server: false, default: () => ({}), getCachedData: key => getStaticCachedData(key, nuxtApp) },
  )

  const { perk, perkStyle, item } = useBuildResolvers(runeTree, itemsMap)

  return { runeTree, itemsMap, perk, perkStyle, item }
}
