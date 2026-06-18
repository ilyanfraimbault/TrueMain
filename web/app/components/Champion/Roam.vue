<script setup lang="ts">
import { formatPercentage } from '~~/shared/utils/ddragon'

const props = withDefaults(defineProps<{
  share: number | null
  games: number
  loading?: boolean
}>(), {
  loading: false,
})

const hasData = computed(() => props.share !== null)
const widthPercent = computed(() => Math.round((props.share ?? 0) * 100))

// 40%+ of early kills out of lane reads as a roamer; 20% or less is lane-bound.
const verdict = computed<{ label: string, tone: string } | null>(() => {
  if (props.share === null) return null
  if (props.share >= 0.4) return { label: 'High roamer', tone: 'text-primary' }
  if (props.share <= 0.2) return { label: 'Lane-focused', tone: 'text-muted' }
  return { label: 'Balanced', tone: 'text-muted' }
})

const PRIMARY = '#34d399' // emerald-400 (CHART_SERIES_PALETTE[0])
</script>

<template>
  <section class="flex flex-col gap-4">
    <header class="flex flex-col gap-0.5">
      <h2 class="text-sm font-semibold">
        Roaming
      </h2>
      <p class="text-xs text-muted">
        Share of early kills and assists made outside the lane.
      </p>
    </header>

    <USkeleton
      v-if="loading"
      class="h-20 w-full rounded-lg"
    />

    <p
      v-else-if="!hasData"
      class="glass rounded-lg px-4 py-8 text-center text-sm text-muted"
    >
      Not enough roam data yet for this champion and lane.
    </p>

    <div
      v-else
      class="glass flex flex-col gap-2 rounded-lg p-4"
    >
      <div class="flex items-baseline justify-between">
        <span class="text-2xl font-semibold tabular-nums">
          {{ formatPercentage(share ?? 0, 0) }}
        </span>
        <span
          v-if="verdict"
          class="text-sm font-medium"
          :class="verdict.tone"
        >
          {{ verdict.label }}
        </span>
      </div>
      <div class="h-2 w-full overflow-hidden rounded-full bg-elevated">
        <div
          class="h-full rounded-full"
          :style="{ width: `${widthPercent}%`, backgroundColor: PRIMARY }"
        />
      </div>
      <p class="text-xs text-muted">
        of early kill participations were out of lane
      </p>
    </div>
  </section>
</template>
