import type { ChampionResponse } from '~~/shared/types/champions'
import { isChampionPosition, type ChampionPosition } from '~/utils/positions'

type Filters = ReturnType<typeof useChampionFilters>['filters']

interface UseChampionDetailStaticsOptions {
  /**
   * Which patch wins in `selectedPatch` while both the loaded champion's
   * patch and the URL filter are set. The global champion page binds to the
   * API-returned patch first so the picker reflects what's actually shown
   * (covering the 404 fallback in useChampion where a dead URL filter is
   * dropped and the API returns its default patch); the player-scoped page
   * keeps the URL filter first. Deliberately NOT unified — each page keeps
   * its historical order.
   */
  preferFilterPatch?: boolean
}

/**
 * Static-data plumbing shared by the global and player-scoped champion detail
 * pages: the patch-pinned static bundles (champion static data, rune tree,
 * items, summoner spells, champion list), the display name / icon fallbacks,
 * and the patch / position selector state derived from the loaded champion +
 * URL filters.
 *
 * The static fetches share keys across both pages (and /champions) so the
 * patch-keyed maps stay deduped across list→detail→list navigations.
 */
export function useChampionDetailStatics(
  championId: MaybeRefOrGetter<number>,
  champion: MaybeRefOrGetter<ChampionResponse | null | undefined>,
  filters: Filters,
  options: UseChampionDetailStaticsOptions = {},
) {
  const championRef = computed(() => toValue(champion) ?? null)

  const activePatch = computed(() => championRef.value?.patch || filters.value.patch || null)

  const { data: staticData, status: staticStatus } = useChampionStatic(championId, activePatch)
  const { data: versions } = useDDragonVersions()

  const { data: staticList, status: staticListStatus } = useChampionStaticList()
  // Pin the rune tree / items / summoner spells to the champion's active
  // patch so the icon URLs we render hit the per-patch (year-cacheable)
  // upstream assets, and so cached payloads don't bleed across patches when
  // the user navigates between them.
  const { data: runeTree, status: runeTreeStatus } = useStaticRuneTree(activePatch)
  const { data: itemsMap, status: itemsStatus } = useStaticItems(activePatch)
  const { data: summonersMap, status: summonersStatus } = useStaticSummonerSpells(activePatch)

  // Fall back to the list-page entry when the per-champion endpoint is still
  // pending or the patch failed to resolve — keeps the header readable
  // instead of flashing the numeric id.
  const championListEntry = computed(() =>
    (staticList.value ?? []).find(item => item.championId === toValue(championId)) ?? null,
  )
  const displayName = computed(() =>
    staticData.value?.championName || championListEntry.value?.name || null,
  )
  const displayIconUrl = computed(() =>
    staticData.value?.championIconUrl || championListEntry.value?.iconUrl || null,
  )

  const patchOptions = usePatchOptions(
    versions,
    () => championRef.value?.patch,
    () => filters.value.patch,
  )

  // See UseChampionDetailStaticsOptions for why the fallback order differs
  // between the two consuming pages.
  const selectedPatch = computed(() => (options.preferFilterPatch
    ? filters.value.patch || championRef.value?.patch
    : championRef.value?.patch || filters.value.patch) || '')

  // Bind to the API-returned position once available so the picker reflects
  // what's actually being shown — covers the 404 fallback in useChampion
  // where the URL filter is dropped and the API returns the default position.
  // Fall back to the URL filter for the optimistic render before the fetch
  // resolves.
  const selectedPosition = computed<ChampionPosition | null>(() => {
    const value = championRef.value?.position || filters.value.position || ''
    return isChampionPosition(value) ? value : null
  })

  return {
    activePatch,
    versions,
    staticData,
    staticStatus,
    staticList,
    staticListStatus,
    runeTree,
    runeTreeStatus,
    itemsMap,
    itemsStatus,
    summonersMap,
    summonersStatus,
    displayName,
    displayIconUrl,
    patchOptions,
    selectedPatch,
    selectedPosition,
  }
}
