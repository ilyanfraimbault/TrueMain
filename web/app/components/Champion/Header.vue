<script setup lang="ts">
import { formatPercentage } from '~~/shared/utils/ddragon'

const props = defineProps<{
  championName: string | null
  championIconUrl: string | null
  championId: number
  position: string
  totalGames: number
  totalWins: number
  // Thin-sample qualifier. When set, a warning icon sits next to the title with
  // this text in its tooltip — mirroring the builder's RecommendationPanel
  // rather than a full-width UAlert.
  lowSampleMessage?: string | null
}>()

const displayName = computed(() => props.championName ?? `Champion ${props.championId}`)
const winRate = computed(() => (props.totalGames === 0 ? 0 : props.totalWins / props.totalGames))
</script>

<template>
  <div class="flex flex-1 flex-wrap items-center gap-4">
    <SkeletonImage
      :src="championIconUrl"
      :alt="championName ?? ''"
      width="80"
      height="80"
      class="size-20 rounded"
    />
    <div class="flex-1">
      <div class="flex items-center gap-2">
        <h1 class="text-2xl font-semibold">
          {{ displayName }}
        </h1>
        <!-- The message lives in the tooltip so it never crowds the header. -->
        <UTooltip
          v-if="lowSampleMessage"
          :text="lowSampleMessage"
          :delay-duration="150"
        >
          <UIcon
            name="i-lucide-triangle-alert"
            class="size-5 text-warning"
          />
        </UTooltip>
      </div>
      <p class="text-sm text-muted">
        {{ position || '—' }} · {{ totalGames }} games · {{ formatPercentage(winRate) }} WR
      </p>
    </div>
  </div>
</template>
