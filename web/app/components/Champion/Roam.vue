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
// Only roamers get the accent tone; the other two stay muted on purpose (the
// label text carries the distinction) so the emphasis lands on the roamers.
const verdict = computed<{ label: string, tone: string } | null>(() => {
  if (props.share === null) return null
  if (props.share >= 0.4) return { label: 'High roamer', tone: 'text-primary' }
  if (props.share <= 0.2) return { label: 'Lane-focused', tone: 'text-muted' }
  return { label: 'Balanced', tone: 'text-muted' }
})

const PRIMARY = '#34d399' // emerald-400 (CHART_SERIES_PALETTE[0])
</script>

<template>
  <SectionCard
    :level="2"
    title="Roaming"
    subtitle="Share of early kills and assists made outside the lane."
  >
    <USkeleton
      v-if="loading"
      class="h-20 w-full rounded-lg"
    />

    <p
      v-else-if="!hasData"
      class="py-8 text-center text-sm text-muted"
    >
      Not enough roam data yet for this champion and lane.
    </p>

    <div
      v-else
      class="flex flex-col gap-2"
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
        of early kill participations were out of lane · {{ games.toLocaleString('en-US') }} games
      </p>
    </div>
  </SectionCard>
</template>
