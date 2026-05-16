<script setup lang="ts">
import type { ChampionSummaryResponse } from '~~/shared/types/champions'
import type { ChampionStaticData } from '~~/shared/types/static-data'
import { formatPercentage } from '~~/shared/utils/ddragon'

const props = defineProps<{
  summary: ChampionSummaryResponse
  championStatic: ChampionStaticData
  championId: number
}>()

const displayName = computed(() => props.championStatic.championName ?? `Champion ${props.championId}`)
</script>

<template>
  <div class="flex flex-1 flex-wrap items-center gap-4">
    <NuxtImg
      v-if="championStatic.championIconUrl"
      :src="championStatic.championIconUrl"
      :alt="championStatic.championName ?? ''"
      width="80"
      height="80"
      class="size-20 rounded"
    />
    <div class="flex-1">
      <h1 class="text-2xl font-semibold">
        {{ displayName }}
      </h1>
      <p class="text-sm text-muted">
        {{ summary.position || '—' }} · {{ summary.games }} games · {{ formatPercentage(summary.winRate) }} WR
      </p>
    </div>
  </div>
</template>
