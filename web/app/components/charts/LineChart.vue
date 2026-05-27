<script setup lang="ts" generic="TItem extends Record<string, unknown>">
import type { BulletLegendItemInterface } from 'vue-chrts/types'
import type { CurveType, LegendPosition } from 'vue-chrts/enums'

// Thin wrapper around `nuxt-charts`'s upstream chart (registered under
// the `Nc` prefix in `nuxt.config.ts` so the wrapper can reference it
// without colliding with its own auto-imported `<ChartsLineChart>` name).
// Purpose:
//   1. apply the TrueMain palette (emerald primary, muted neutral guides)
//      so callers don't repeat hex codes on every chart;
//   2. fix the container size so an empty/loading state never collapses
//      the surrounding layout (matches the pattern from issue #192);
//   3. keep a stable surface so we can swap charting libraries later
//      without touching every consumer.

interface Props {
  data: TItem[]
  categories: Record<string, BulletLegendItemInterface>
  height?: number
  loading?: boolean
  emptyMessage?: string
  xLabel?: string
  yLabel?: string
  xFormatter?: (tick: number | Date, i: number, ticks: (number | Date)[]) => string
  yFormatter?: (tick: number | Date, i: number, ticks: (number | Date)[]) => string
  curveType?: CurveType
  lineWidth?: number
  yGridLine?: boolean
  hideLegend?: boolean
  legendPosition?: LegendPosition
}

const props = withDefaults(defineProps<Props>(), {
  height: 240,
  loading: false,
  emptyMessage: 'No data available',
  lineWidth: 2.5,
  yGridLine: true,
})

const resolvedCategories = computed(() => {
  const out: Record<string, BulletLegendItemInterface> = {}
  Object.keys(props.categories).forEach((key, i) => {
    const cat = props.categories[key]!
    out[key] = { ...cat, color: cat.color ?? defaultSeriesColor(i) }
  })
  return out
})

const isEmpty = computed(
  () => !props.loading && (props.data?.length ?? 0) === 0,
)

const crosshairConfig = { color: CHART_GUIDE_COLOR }
</script>

<template>
  <div
    :style="{ height: `${height}px` }"
    class="relative w-full"
  >
    <USkeleton v-if="loading" class="absolute inset-0 size-full" />
    <div
      v-else-if="isEmpty"
      class="absolute inset-0 flex items-center justify-center text-sm text-muted"
    >
      {{ emptyMessage }}
    </div>
    <ClientOnly v-else>
      <NcLineChart
        :data="data"
        :categories="resolvedCategories"
        :height="height"
        :x-label="xLabel"
        :y-label="yLabel"
        :x-formatter="xFormatter"
        :y-formatter="yFormatter"
        :curve-type="curveType"
        :line-width="lineWidth"
        :y-grid-line="yGridLine"
        :hide-legend="hideLegend"
        :legend-position="legendPosition"
        :crosshair-config="crosshairConfig"
      />
      <template #fallback>
        <USkeleton class="absolute inset-0 size-full" />
      </template>
    </ClientOnly>
  </div>
</template>
