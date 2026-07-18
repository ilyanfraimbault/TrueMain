<script setup lang="ts" generic="TItem extends Record<string, unknown>">
import type { axisFormatter, BulletLegendItemInterface } from 'vue-chrts/types'
import type { LegendPosition } from 'vue-chrts/enums'

// Sibling of <ChartsLineChart> / <ChartsAreaChart> for bar charts. Same
// defaults — rose-gold palette, fixed container, decoupled API — so bar
// consumers stay visually consistent with the line/area charts. The upstream
// chart is referenced as <NcBarChart> (see `nuxtCharts.prefix` in
// nuxt.config.ts) to avoid self-resolving against this wrapper's own
// auto-imported name.

interface Props {
  data: TItem[]
  categories: Record<string, BulletLegendItemInterface>
  /** Data keys plotted as bars, one per category. */
  yAxis: (keyof TItem)[]
  height?: number
  loading?: boolean
  emptyMessage?: string
  xLabel?: string
  yLabel?: string
  xFormatter?: axisFormatter
  yFormatter?: axisFormatter
  /** Fractional padding between bars in [0,1). */
  barPadding?: number
  radius?: number
  xGridLine?: boolean
  yGridLine?: boolean
  hideLegend?: boolean
  hideXAxis?: boolean
  hideYAxis?: boolean
  legendPosition?: LegendPosition
}

const props = withDefaults(defineProps<Props>(), {
  height: 240,
  loading: false,
  emptyMessage: 'No data available',
  radius: 4,
  xGridLine: false,
  yGridLine: true,
})

defineSlots<{
  tooltip?(props: { values: TItem | undefined }): unknown
}>()

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
      <NcBarChart
        :data="data"
        :categories="resolvedCategories"
        :y-axis="yAxis"
        :height="height"
        :x-label="xLabel"
        :y-label="yLabel"
        :x-formatter="xFormatter"
        :y-formatter="yFormatter"
        :bar-padding="barPadding"
        :radius="radius"
        :x-grid-line="xGridLine"
        :y-grid-line="yGridLine"
        :hide-legend="hideLegend"
        :hide-x-axis="hideXAxis"
        :hide-y-axis="hideYAxis"
        :legend-position="legendPosition"
      >
        <template v-if="$slots.tooltip" #tooltip="{ values }">
          <slot name="tooltip" :values="(values as TItem | undefined)" />
        </template>
      </NcBarChart>
      <template #fallback>
        <USkeleton class="absolute inset-0 size-full" />
      </template>
    </ClientOnly>
  </div>
</template>
