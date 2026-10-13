<script setup lang="ts">
// One series of the trend-by-patch card: a titled area chart over the patches,
// its x axis labelled with the patch of each point.
const props = defineProps<{
  title: string
  /** One row per patch, oldest first, carrying `valueKey`. */
  rows: ({ patch: string } & Record<string, unknown>)[]
  valueKey: string
  format: (value: number) => string
}>()

// Every series in the app primary (rosegold-400). The charts sit side by side
// and never share a plot, so they don't need different hues to be told apart.
const categories = computed(() => ({ [props.valueKey]: { name: props.title, color: defaultSeriesColor(0) } }))

// Soft top-down fade for the filled area, matching ProfileRankedCard.
const gradientStops = [
  { offset: '0%', stopOpacity: 0.4 },
  { offset: '100%', stopOpacity: 0.04 },
]

// The tick indices are this series' own: the ban series is filtered, so it does
// not share the others' x axis.
const xFormatter = (tick: number): string => props.rows[tick]?.patch ?? ''

// Without explicit ticks the axis scale places ticks at fractional positions
// (0, 0.5, 1, ...), and the formatter — which indexes the row array — returns an
// empty string for every one of them but the first: the axis ended up labelled
// with a single patch. Pin the ticks to data points instead, at most five so a
// narrow column stays readable, always keeping the first and the last patch so
// the range the chart covers is obvious.
const MAX_X_TICKS = 5

const xTicks = computed(() => {
  const count = props.rows.length
  if (count <= MAX_X_TICKS) {
    return Array.from({ length: count }, (_, i) => i)
  }
  const step = (count - 1) / (MAX_X_TICKS - 1)
  const ticks = Array.from({ length: MAX_X_TICKS }, (_, i) => Math.round(i * step))
  return [...new Set(ticks)]
})
</script>

<template>
  <div class="flex flex-col gap-1.5">
    <h3 class="text-xs font-medium text-muted">
      {{ title }}
    </h3>
    <ChartsAreaChart
      :data="rows"
      :categories="categories"
      :height="220"
      :x-formatter="xFormatter"
      :x-explicit-ticks="xTicks"
      :y-formatter="format"
      :gradient-stops="gradientStops"
      :y-grid-line="false"
      hide-legend
    >
      <template #tooltip="{ values }">
        <div
          v-if="values"
          class="rounded-md border border-default bg-elevated px-2 py-1.5 text-xs shadow-md"
        >
          <p class="font-semibold tabular-nums text-default">
            {{ format(values[valueKey] as number) }}
          </p>
          <p class="mt-0.5 text-muted">
            Patch {{ values.patch }}
          </p>
        </div>
      </template>
    </ChartsAreaChart>
  </div>
</template>
