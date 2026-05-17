<script setup lang="ts">
import type { ChampionStaticData } from '~~/shared/types/static-data'
import { formatPercentage } from '~~/shared/utils/ddragon'

const props = defineProps<{
  championStatic: ChampionStaticData
  championId: number
  position: string
  totalGames: number
  totalWins: number
}>()

const displayName = computed(() => props.championStatic.championName ?? `Champion ${props.championId}`)
const winRate = computed(() => (props.totalGames === 0 ? 0 : props.totalWins / props.totalGames))
</script>

<template>
  <div class="flex flex-1 flex-wrap items-center gap-4">
    <SkeletonImage
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
        {{ position || '—' }} · {{ totalGames }} games · {{ formatPercentage(winRate) }} WR
      </p>
    </div>
  </div>
</template>
