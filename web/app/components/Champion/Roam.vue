<script setup lang="ts">
const props = withDefaults(defineProps<{
  kp5: number | null
  kp10: number | null
  kp15: number | null
  games: number
  loading?: boolean
}>(), {
  loading: false,
})

// The @15 window is the headline (it's cumulative, so it's the largest); its
// presence also signals whether we have data at all.
const hasData = computed(() => props.kp15 !== null)

const windows = computed(() => [
  { label: '@5', value: props.kp5 },
  { label: '@10', value: props.kp10 },
  { label: '@15', value: props.kp15 },
])

const fmt = (value: number | null) => (value === null ? '–' : value.toFixed(1))

// Bar heights share one scale so the three windows read as a single ramp and
// stay comparable across champions. The 1.5 reference is the "high roamer"
// threshold, so a full bar means "roams as much as a dedicated roamer".
const BAR_SCALE = 1.5

const barHeight = (value: number | null) => {
  if (!value || value <= 0) return '0%'
  return `${Math.min(100, (value / BAR_SCALE) * 100)}%`
}

// Verdict on the @15 average (out-of-lane kills + assists per game). Heuristic
// thresholds: 1.5+ reads as a roamer, 0.5 or fewer as lane-bound. Only roamers
// get the accent tone so the emphasis lands on them; the others stay muted.
const verdict = computed<{ label: string, tone: string } | null>(() => {
  if (props.kp15 === null) return null
  if (props.kp15 >= 1.5) return { label: 'High roamer', tone: 'text-primary' }
  if (props.kp15 <= 0.5) return { label: 'Lane-focused', tone: 'text-muted' }
  return { label: 'Balanced', tone: 'text-muted' }
})
</script>

<template>
  <section class="flex flex-col gap-4">
    <header class="flex flex-col gap-0.5">
      <h2 class="text-sm font-semibold">
        Roaming
      </h2>
      <p class="text-xs text-muted">
        Average out-of-lane kills + assists per game, by minute mark.
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
      class="glass flex flex-col gap-4 rounded-lg p-4"
    >
      <div class="flex items-baseline justify-between gap-2">
        <p class="text-xs text-muted">
          {{ games.toLocaleString('en-US') }} games analysed
        </p>
        <span
          v-if="verdict"
          class="text-sm font-medium"
          :class="verdict.tone"
        >
          {{ verdict.label }}
        </span>
      </div>

      <div class="flex items-end gap-4">
        <div
          v-for="window in windows"
          :key="window.label"
          class="flex flex-1 flex-col items-center gap-2"
        >
          <span
            class="text-xl font-semibold tabular-nums"
            :class="window.value === null ? 'text-muted' : ''"
          >
            {{ fmt(window.value) }}
          </span>
          <div class="relative h-16 w-full overflow-hidden rounded-md bg-elevated">
            <div
              class="absolute inset-x-0 bottom-0 rounded-md bg-primary/70 transition-[height] duration-500"
              :style="{ height: barHeight(window.value) }"
            />
          </div>
          <span class="text-xs text-muted">
            {{ window.label }} min
          </span>
        </div>
      </div>
    </div>
  </section>
</template>
