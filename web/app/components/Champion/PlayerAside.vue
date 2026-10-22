<script setup lang="ts">
import type { ProfileRanked } from '#shared/types/profile'
import type { RankHistoryEntry } from '#shared/types/rank-history'
import type { PositionFilter } from '#common/utils/positions'

// The player-scoped champion page's cards beside the build: the ranked card the
// profile opens its column on (it renders even when the champion aggregate
// failed — it never depended on it) and their performance on the champion. The
// matchups moved to the main column as a spread (the page itself).
const props = defineProps<{
  /** Null until the profile has loaded: the ranked card skeletons. */
  ranked: ProfileRanked | null | undefined
  profileLoaded: boolean
  history: readonly RankHistoryEntry[]
  historyLoading: boolean
  /** The player's platform, for the LP curve's Grandmaster / Challenger lines (#2174). */
  platformId: string | null
  /** The champion aggregate failed: only the ranked card stays. */
  championFailed: boolean
  nameTag: string
  championId: number
  patch: string
  position: PositionFilter | null
  playerName: string
  championName: string | null
}>()

const apexCutoffs = useApexCutoffs(() => props.platformId)

// Frozen prop bundle for the lazy performance card (#918), whose only client-only
// inputs are the two display names it prints. `patch`/`position` stay live — they feed the
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
    :history-loading="historyLoading || apexCutoffs.loading"
    :cutoffs="apexCutoffs.cutoffs"
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
</template>
