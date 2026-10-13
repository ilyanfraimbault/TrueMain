<script setup lang="ts">
import type { ProfileRanked } from '#shared/types/profile'
import type { RankHistoryEntry } from '#shared/types/rank-history'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import type { ChampionPosition } from '#common/utils/positions'

// The player-scoped champion page's cards beside the build: the ranked card the
// profile opens its column on (it renders even when the champion aggregate
// failed — it never depended on it), their performance on the champion, its
// matchups.
const props = defineProps<{
  /** Null until the profile has loaded: the ranked card skeletons. */
  ranked: ProfileRanked | null | undefined
  profileLoaded: boolean
  history: readonly RankHistoryEntry[]
  historyLoading: boolean
  /** The champion aggregate failed: only the ranked card stays. */
  championFailed: boolean
  nameTag: string
  championId: number
  patch: string
  position: ChampionPosition | null
  playerName: string
  championName: string | null
  champions: ChampionStaticListItem[] | null | undefined
}>()

// Frozen prop bundle for the lazy (hydrate-on-visible) matchups card — see
// useLazyHydrationSnapshot / the identical pattern on pages/champions/[slug].vue
// for why: `staticList` is client-only (`server: false`), so freezing it
// until the child actually mounts avoids a hydration mismatch (#834/#837).
const matchupsSnapshot = useLazyHydrationSnapshot(
  { champions: [] as ChampionStaticListItem[] },
  () => ({ champions: props.champions ?? [] }),
)

// And for the performance card (#918), whose only client-only inputs are the
// two display names it prints. `patch`/`position` stay live — they feed the
// fetch key, never the SSR markup, so they can't desynchronise hydration.
const performanceSnapshot = useLazyHydrationSnapshot(
  { playerName: props.nameTag, championName: null as string | null },
  () => ({ playerName: props.playerName, championName: props.championName ?? null }),
)
</script>

<template>
  <ProfileRankedCardSkeleton v-if="!profileLoaded" />
  <ProfileRankedCard
    v-else
    :ranked="ranked ?? null"
    :history="history"
    :history-loading="historyLoading"
  />

  <!-- Performance score (#918), lazy + hydrate-on-visible with the
       client-only display names frozen until it mounts (#834/#837). -->
  <LazyChampionPlayerPerformance
    v-if="!championFailed"
    hydrate-on-visible
    :name-tag="nameTag"
    :champion-id="championId"
    :patch="patch"
    :position="position"
    v-bind="performanceSnapshot.value"
    @vue:mounted="performanceSnapshot.reveal"
  />

  <!-- Lazy + hydrate-on-visible (#820), `:champions` frozen (#834/#837).
       Cross-patch on purpose: one player's games under a patch filter
       would leave nearly every opponent under the 3-game floor. -->
  <LazyChampionMatchups
    v-if="!championFailed"
    hydrate-on-visible
    :champion-id="championId"
    :position="position"
    :name-tag="nameTag"
    v-bind="matchupsSnapshot.value"
    @vue:mounted="matchupsSnapshot.reveal"
  />
</template>
