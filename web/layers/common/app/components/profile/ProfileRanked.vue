<script setup lang="ts">
import type { ProfileRankedReadModel, RankHistoryEntryReadModel } from '#shared/contract/api'

// The ranked card as every player page opens its column on it: a skeleton until
// the profile lands, then the card with the LP curve and the region's
// Grandmaster / Challenger lines (#2174).
const props = defineProps<{
  /** Null for an unranked account; ignored while `loading`. */
  ranked: ProfileRankedReadModel | null | undefined
  loading: boolean
  history: readonly RankHistoryEntryReadModel[]
  historyLoading: boolean
  /** The player's platform, which picks the cutoff lines. */
  platformId: string | null | undefined
}>()

const apexCutoffs = useApexCutoffs(() => props.platformId)
</script>

<template>
  <ProfileRankedCardSkeleton v-if="loading" />
  <ProfileRankedCard
    v-else
    :ranked="ranked ?? null"
    :history="history"
    :history-loading="historyLoading || apexCutoffs.loading"
    :cutoffs="apexCutoffs.cutoffs"
  />
</template>
