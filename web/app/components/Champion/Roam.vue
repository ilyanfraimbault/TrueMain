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
      class="glass flex flex-col gap-3 rounded-lg p-4"
    >
      <div class="flex items-center justify-between">
        <span class="text-xs font-medium uppercase tracking-wide text-muted">
          Roaming KP
        </span>
        <span
          v-if="verdict"
          class="text-sm font-medium"
          :class="verdict.tone"
        >
          {{ verdict.label }}
        </span>
      </div>

      <div class="grid grid-cols-3 gap-2">
        <div
          v-for="window in windows"
          :key="window.label"
          class="flex flex-col items-center gap-0.5 rounded-lg bg-elevated px-2 py-3"
        >
          <span class="text-2xl font-semibold tabular-nums">
            {{ fmt(window.value) }}
          </span>
          <span class="text-xs text-muted">
            {{ window.label }}
          </span>
        </div>
      </div>

      <p class="text-xs text-muted">
        out-of-lane kill participations per game · {{ games.toLocaleString('en-US') }} games
      </p>
    </div>
  </section>
</template>
