<!--
  App-specific stand-in for `web/app/components/charts/AreaChart.vue`, which
  wraps vue-chrts (Unovis). The app draws the one area chart it has — the
  ranked card's LP curve — in plain SVG instead of shipping that stack in a
  webview. Same props and tooltip slot, for the subset the twinned
  `ProfileRankedCard` passes; the line and the area carry Unovis' class
  suffixes (`-linePath`, `-area`) so the card's scoped repaint (tier gradient,
  vertical fade) lands on them unchanged.
-->
<script setup lang="ts" generic="TItem extends Record<string, unknown>">
interface Props {
  data: TItem[]
  categories: Record<string, { name: string, color?: string | string[] }>
  height?: number
  xFormatter?: (tick: number) => string
  yDomain?: [number | undefined, number | undefined]
  hideYAxis?: boolean
  hideLegend?: boolean
}

const props = withDefaults(defineProps<Props>(), { height: 240, xFormatter: undefined, yDomain: undefined })

defineSlots<{
  tooltip?(props: { values: TItem | undefined }): unknown
}>()

/** Room under the plot for the date ticks. */
const AXIS = 18
const MAX_TICKS = 4

const box = ref<HTMLElement | null>(null)
const width = ref(0)
let observer: ResizeObserver | null = null
onMounted(() => {
  observer = new ResizeObserver(([entry]) => (width.value = entry?.contentRect.width ?? 0))
  if (box.value) observer.observe(box.value)
})
onBeforeUnmount(() => observer?.disconnect())

const key = computed(() => Object.keys(props.categories)[0] ?? '')
const color = computed(() => {
  const value = props.categories[key.value]?.color
  return (Array.isArray(value) ? value[0] : value) ?? 'var(--ui-primary)'
})
const plotHeight = computed(() => props.height - AXIS)
const values = computed(() => props.data.map(item => Number(item[key.value] ?? 0)))

const domain = computed<[number, number]>(() => {
  const min = props.yDomain?.[0] ?? Math.min(...values.value)
  const max = props.yDomain?.[1] ?? Math.max(...values.value)
  return max > min ? [min, max] : [min - 1, max + 1]
})

const points = computed(() => {
  const count = values.value.length
  const [min, max] = domain.value
  return values.value.map((value, index) => ({
    x: count === 1 ? width.value / 2 : (index / (count - 1)) * width.value,
    y: (1 - (value - min) / (max - min)) * plotHeight.value,
  }))
})

/** One snapshot draws as a level line across the plot, not as a lone dot. */
const drawn = computed(() => {
  const only = points.value.length === 1 ? points.value[0] : null
  return only ? [{ x: 0, y: only.y }, { x: width.value, y: only.y }] : points.value
})
const line = computed(() => drawn.value.map((p, i) => `${i ? 'L' : 'M'}${p.x.toFixed(1)},${p.y.toFixed(1)}`).join(''))
const area = computed(() => {
  const first = drawn.value[0]
  const last = drawn.value.at(-1)
  return first && last ? `${line.value}L${last.x},${plotHeight.value}L${first.x},${plotHeight.value}Z` : ''
})

const ticks = computed(() => {
  const count = points.value.length
  if (!props.xFormatter || count === 0) return []
  const step = Math.max(1, Math.ceil(count / MAX_TICKS))
  const indices = Array.from({ length: count }, (_, i) => i).filter(i => i % step === 0)
  return indices.map(index => ({ x: points.value[index]!.x, label: props.xFormatter!(index) }))
})

const hovered = ref<number | null>(null)
function hover(event: MouseEvent) {
  const count = points.value.length
  if (!count || !width.value) return
  const ratio = event.offsetX / width.value
  hovered.value = count === 1 ? 0 : Math.round(Math.min(Math.max(ratio, 0), 1) * (count - 1))
}
const hoveredPoint = computed(() => (hovered.value === null ? null : points.value[hovered.value] ?? null))
</script>

<template>
  <div ref="box" class="relative w-full" :style="{ height: `${height}px` }">
    <svg
      v-if="width && points.length"
      :width="width"
      :height="height"
      class="overflow-visible"
      @mousemove="hover"
      @mouseleave="hovered = null"
    >
      <!-- Opaque, as Unovis draws it: the caller fades it (the ranked card masks it). -->
      <path :d="area" class="chart-area" :fill="color" />
      <path :d="line" class="chart-linePath" fill="none" :stroke="color" stroke-width="2" stroke-linejoin="round" />
      <template v-if="hoveredPoint">
        <line :x1="hoveredPoint.x" :x2="hoveredPoint.x" y1="0" :y2="plotHeight" stroke="var(--ui-border-accented)" stroke-dasharray="3 3" />
        <circle :cx="hoveredPoint.x" :cy="hoveredPoint.y" r="3.5" :fill="color" stroke="var(--ui-bg)" stroke-width="1.5" />
      </template>
      <text
        v-for="tick in ticks"
        :key="tick.x"
        :x="tick.x"
        :y="height - 4"
        :text-anchor="tick.x < 12 ? 'start' : tick.x > width - 12 ? 'end' : 'middle'"
        class="fill-(--ui-text-dimmed) text-[10px]"
      >{{ tick.label }}</text>
    </svg>
    <div
      v-if="hoveredPoint && $slots.tooltip"
      class="pointer-events-none absolute z-10 -translate-x-1/2 -translate-y-full"
      :style="{ left: `${Math.min(Math.max(hoveredPoint.x, 48), width - 48)}px`, top: `${hoveredPoint.y - 8}px` }"
    >
      <slot name="tooltip" :values="data[hovered!]" />
    </div>
  </div>
</template>
