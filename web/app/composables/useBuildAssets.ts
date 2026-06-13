import type { RuneTreeResponse, StaticItemData, StaticPerkData, StaticPerkStyleData } from '~~/shared/types/static-data'

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

  const { data: runeTree } = useLazyAsyncData<RuneTreeResponse | null>(
    () => `rune-tree-${patchRef.value || 'latest'}`,
    async () => {
      const key = `rune-tree-${patchRef.value || 'latest'}`
      const data = await $fetch<RuneTreeResponse>('/api/static/rune-tree', {
        query: patchRef.value ? { patch: patchRef.value } : {},
      })
      markStaticFetched(key, nuxtApp)
      return data
    },
    { watch: [patchRef], server: false, default: () => null, getCachedData: key => getStaticCachedData(key, nuxtApp) },
  )

  const { data: itemsMap } = useLazyAsyncData<Record<number, StaticItemData>>(
    () => `static-items-${patchRef.value || 'latest'}`,
    async () => {
      const key = `static-items-${patchRef.value || 'latest'}`
      const data = await $fetch<Record<number, StaticItemData>>('/api/static/items', {
        query: patchRef.value ? { patch: patchRef.value } : {},
      })
      markStaticFetched(key, nuxtApp)
      return data
    },
    { watch: [patchRef], server: false, default: () => ({}), getCachedData: key => getStaticCachedData(key, nuxtApp) },
  )

  function perk(id: number | null | undefined): StaticPerkData | null {
    if (!id) return null
    return runeTree.value?.perks?.[id] ?? null
  }
  function perkStyle(id: number | null | undefined): StaticPerkStyleData | null {
    if (!id) return null
    return runeTree.value?.perkStyles?.[id] ?? null
  }
  function item(id: number | null | undefined): StaticItemData | null {
    if (!id) return null
    return itemsMap.value?.[id] ?? null
  }

  return { runeTree, itemsMap, perk, perkStyle, item }
}
