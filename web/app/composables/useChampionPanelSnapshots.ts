import type { ChampionScalingBucket, ChampionTrendPoint } from '~~/shared/types/champions'
import type { ChampionStaticListItem, StaticItemData } from '~~/shared/types/static-data'

/**
 * Lazy-hydration snapshots for the champion detail page's below-the-fold
 * panels.
 *
 * Those charts/panels are `hydrate-on-visible` (their JS is heavy —
 * nuxt-charts — so it's kept out of the initial hydration pass, #820) but
 * every value they render comes from client-only (`server: false`)
 * composables. SSR always renders their empty/loading state; without
 * freezing, a child's *deferred* hydration (on scroll, well after the
 * client-only fetches have resolved) would reconcile against that stale SSR
 * snapshot using already-loaded data — a hydration mismatch on every one of
 * them, forcing Vue to discard and rebuild each subtree exactly as it enters
 * the viewport (#834/#837 — that's what caused the reported scroll jank).
 * `useLazyHydrationSnapshot` keeps each child's first (hydration) render
 * identical to SSR; `@vue:mounted="…Snapshot.reveal"` on the child then swaps
 * in the live, reactive value as a normal post-hydration update.
 */
export function useChampionPanelSnapshots(sources: {
  trend: Ref<{ points: ChampionTrendPoint[] } | null | undefined>
  trendPending: Ref<boolean>
  scaling: Ref<{ buckets: ChampionScalingBucket[], scalingIndex: number | null } | null | undefined>
  scalingPending: Ref<boolean>
  staticList: Ref<ChampionStaticListItem[] | null | undefined>
  itemsMap: Ref<Record<number, StaticItemData> | null | undefined>
  latestVersion: Ref<string | null>
}) {
  const { trend, trendPending, scaling, scalingPending, staticList, itemsMap, latestVersion } = sources

  const trendSnapshot = useLazyHydrationSnapshot(
    { points: [] as ChampionTrendPoint[], loading: true },
    () => ({ points: trend.value?.points ?? [], loading: trendPending.value }),
  )
  const scalingSnapshot = useLazyHydrationSnapshot(
    { buckets: [] as ChampionScalingBucket[], scalingIndex: null as number | null, loading: true },
    () => ({
      buckets: scaling.value?.buckets ?? [],
      scalingIndex: scaling.value?.scalingIndex ?? null,
      loading: scalingPending.value,
    }),
  )
  const truemainsSnapshot = useLazyHydrationSnapshot(
    { champions: [] as ChampionStaticListItem[], itemsMap: {} as Record<number, StaticItemData>, patch: null as string | null },
    () => ({ champions: staticList.value ?? [], itemsMap: itemsMap.value ?? {}, patch: latestVersion.value }),
  )
  const matchupsSnapshot = useLazyHydrationSnapshot(
    { champions: [] as ChampionStaticListItem[] },
    () => ({ champions: staticList.value ?? [] }),
  )
  const synergiesSnapshot = useLazyHydrationSnapshot(
    { champions: [] as ChampionStaticListItem[] },
    () => ({ champions: staticList.value ?? [] }),
  )

  return { trendSnapshot, scalingSnapshot, truemainsSnapshot, matchupsSnapshot, synergiesSnapshot }
}
