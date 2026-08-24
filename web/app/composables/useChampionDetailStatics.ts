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
  /**
   * Whether the champion fetch has settled (success or error). Gates the
   * "latest DDragon version" fallback in `activePatch` — see the comment
   * there. Callers that omit it get the fallback as soon as the champion is
   * absent, which costs a throwaway `latest` round trip on a cold load; both
   * champion detail pages pass their own `championStatus` instead.
   */
  championSettled?: MaybeRefOrGetter<boolean>
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
  const { data: versions } = useDDragonVersions()

  /**
   * The patch every static bundle below is pinned to. It normally comes from
   * the loaded champion, or from the URL filter before that lands — but a
   * champion we hold no aggregate for (404 → `notEnoughData`) never produces
   * one, and an unfiltered URL has none either. Since the deferred fetches
   * below only fire on a non-null patch, that combination used to park the
   * rune tree / items / summoner spells in `idle` forever: the loading bar
   * never stopped and the static bundle the match rows wait on never arrived.
   *
   * So once the champion fetch has *settled* without giving us a patch, fall
   * back to the latest DDragon version. Gating on settled-ness is what keeps
   * `immediate: false` worth having: while the champion is still in flight a
   * real patch may still be coming, and fetching under `latest` then refetching
   * under it is exactly the double round trip the deferral exists to avoid.
   */
  const championSettled = computed(() => toValue(options.championSettled) ?? true)
  const activePatch = computed(() =>
    championRef.value?.patch
    || filters.value.patch
    || (championSettled.value ? versions.value?.[0] : null)
    || null,
  )

  const { data: staticData, status: staticStatus } = useChampionStatic(championId, activePatch)

  const { data: staticList, status: staticListStatus } = useChampionStaticList()
  // Pin the rune tree / items / summoner spells to the champion's active
  // patch so the icon URLs we render hit the per-patch (year-cacheable)
  // upstream assets, and so cached payloads don't bleed across patches when
  // the user navigates between them.
  //
  // `immediate: false` + the watcher below defers each first fetch until
  // `activePatch` resolves, so we don't issue a throwaway `latest` round trip
  // (these payloads are large — items alone is ~370 KiB) and immediately
  // refetch under the resolved patch key. The `unresolvedKeySegment` guarantees
  // the pre-resolution key can never collide with a real `latest` cache entry.
  const staticFetchOptions = { immediate: false, unresolvedKeySegment: 'pending' } as const
  const {
    data: runeTree,
    status: runeTreeStatus,
    execute: fetchRuneTree,
  } = useStaticRuneTree(activePatch, staticFetchOptions)
  const {
    data: itemsMap,
    status: itemsStatus,
    execute: fetchItems,
  } = useStaticItems(activePatch, staticFetchOptions)
  const {
    data: summonersMap,
    status: summonersStatus,
    execute: fetchSummoners,
  } = useStaticSummonerSpells(activePatch, staticFetchOptions)

  // Kick off the deferred static fetches once (and each time) the patch
  // resolves. `immediate: true` so it fires synchronously when `activePatch`
  // is already known on mount; the `patch` guard keeps it a no-op while the
  // patch is still null, so no request goes out under the unresolved key.
  watch(activePatch, (patch) => {
    if (!patch) return
    void fetchRuneTree()
    void fetchItems()
    void fetchSummoners()
  }, { immediate: true })

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
