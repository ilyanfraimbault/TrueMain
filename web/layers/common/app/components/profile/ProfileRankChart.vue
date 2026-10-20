<script setup lang="ts">
import type { ApexCutoff } from '#shared/types/apex-cutoffs'
import type { RankChartPoint } from '#common/utils/rank-chart'
import type { PlotFrame } from '#common/utils/rank-chart-geometry'
import { monotonePath } from '#common/utils/monotone-path'
import { rankChartDateLabel } from '#common/utils/rank-chart'
import {
  boundaryLinePath,
  crestBands,
  scoreY,
  zoneClipPath,
  zoneVisible,
} from '#common/utils/rank-chart-geometry'
import { apexFloorsAt, rankChartYDomain, tierBoundaries, tierZones } from '#common/utils/rank-chart-zones'
import { tierHex } from '#common/utils/tiers'

// The ranked card's LP curve, in plain SVG on both hosts. The curve is drawn
// once per tier band and clipped to it, so its colour changes where it crosses
// a threshold — fixed below Master, the day's Grandmaster / Challenger cutoff
// above it (`utils/rank-chart-zones.ts`) — and the thresholds in view are drawn
// as dashed lines, with the tier crests as the y-axis.
const props = withDefaults(defineProps<{
  points: RankChartPoint[]
  cutoffs?: readonly ApexCutoff[]
}>(), {
  cutoffs: () => [],
})

const HEIGHT = 150
/** Room under the plot for the date ticks. */
const AXIS = 18
const PLOT = HEIGHT - AXIS

const box = ref<HTMLElement | null>(null)
const width = ref(0)
let observer: ResizeObserver | null = null
onMounted(() => {
  observer = new ResizeObserver(([entry]) => (width.value = entry?.contentRect.width ?? 0))
  if (box.value) observer.observe(box.value)
})
onBeforeUnmount(() => observer?.disconnect())

// One snapshot draws as a level line across the plot, not as a lone dot.
const series = computed(() => (props.points.length === 1 ? [props.points[0]!, props.points[0]!] : props.points))
const floors = computed(() => apexFloorsAt(series.value, props.cutoffs))
const boundaries = computed(() => tierBoundaries(series.value, floors.value))
const domain = computed(() => rankChartYDomain(series.value, boundaries.value))

const frame = computed<PlotFrame>(() => {
  const count = series.value.length
  return {
    xs: series.value.map((_, i) => (count === 1 ? width.value / 2 : (i / (count - 1)) * width.value)),
    height: PLOT,
    domain: domain.value,
  }
})

const curve = computed(() => series.value.map((point, i) => ({ x: frame.value.xs[i]!, y: scoreY(point.score, frame.value) })))
const line = computed(() => monotonePath(curve.value))
const area = computed(() => {
  const first = curve.value[0]
  const last = curve.value.at(-1)
  return first && last ? `${line.value}L${last.x},${PLOT}L${first.x},${PLOT}Z` : ''
})

const uid = useId()
const zones = computed(() => tierZones(boundaries.value)
  .filter(zone => zoneVisible(zone, frame.value))
  .map(zone => ({
    tier: zone.tier,
    color: tierHex(zone.tier),
    clipId: `rank-zone-${uid}-${zone.tier}`,
    clip: zoneClipPath(zone, frame.value),
  })))

const thresholds = computed(() => boundaries.value
  .map(boundary => ({ tier: boundary.tier, color: tierHex(boundary.tier), path: boundaryLinePath(boundary, frame.value) }))
  .filter(threshold => threshold.path))

const crests = computed(() => crestBands(tierZones(boundaries.value), frame.value))

const fadeId = `rank-fade-${uid}`
const fadeMaskId = `rank-fade-mask-${uid}`

const ticks = computed(() => {
  const count = props.points.length
  if (count === 0) return []
  const xs = frame.value.xs
  const ends = count === 1 ? [{ x: width.value / 2, index: 0 }] : [{ x: xs[0]!, index: 0 }, { x: xs.at(-1)!, index: count - 1 }]
  return ends.map(end => ({ x: end.x, label: rankChartDateLabel(props.points[end.index]!.entry.capturedAtUtc) }))
})

const hovered = ref<number | null>(null)
function hover(event: MouseEvent) {
  const count = props.points.length
  if (!count || !width.value) return
  const ratio = Math.min(Math.max(event.offsetX / width.value, 0), 1)
  hovered.value = count === 1 ? 0 : Math.round(ratio * (count - 1))
}
const hoveredDot = computed(() => {
  if (hovered.value === null) return null
  const index = props.points.length === 1 ? 0 : hovered.value
  const point = curve.value[index]
  return point ? { ...point, color: tierHex(props.points[hovered.value]!.entry.tier) } : null
})
</script>

<template>
  <div class="flex gap-2">
    <ProfileRankCrestAxis :crests="crests" :height="PLOT" />

    <div ref="box" class="relative min-w-0 flex-1" :style="{ height: `${HEIGHT}px` }">
      <svg
        v-if="width && points.length"
        :width="width"
        :height="HEIGHT"
        class="overflow-visible"
        @mousemove="hover"
        @mouseleave="hovered = null"
      >
        <defs>
          <clipPath v-for="zone in zones" :id="zone.clipId" :key="zone.clipId">
            <path :d="zone.clip" />
          </clipPath>
          <linearGradient :id="fadeId" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stop-color="white" stop-opacity="0.45" />
            <stop offset="100%" stop-color="white" stop-opacity="0.05" />
          </linearGradient>
          <mask :id="fadeMaskId" maskContentUnits="objectBoundingBox">
            <rect x="0" y="0" width="1" height="1" :fill="`url(#${fadeId})`" />
          </mask>
        </defs>

        <g v-for="zone in zones" :key="zone.tier" :clip-path="`url(#${zone.clipId})`">
          <path :d="area" :fill="zone.color" :mask="`url(#${fadeMaskId})`" />
          <path :d="line" fill="none" :stroke="zone.color" stroke-width="2" stroke-linejoin="round" />
        </g>

        <path
          v-for="threshold in thresholds"
          :key="threshold.tier"
          :d="threshold.path"
          fill="none"
          :stroke="threshold.color"
          stroke-opacity="0.55"
          stroke-width="1"
          stroke-dasharray="3 3"
        />

        <template v-if="hoveredDot">
          <line :x1="hoveredDot.x" :x2="hoveredDot.x" y1="0" :y2="PLOT" stroke="var(--ui-border-accented)" stroke-dasharray="3 3" />
          <circle :cx="hoveredDot.x" :cy="hoveredDot.y" r="3.5" :fill="hoveredDot.color" stroke="var(--ui-bg)" stroke-width="1.5" />
        </template>

        <text
          v-for="tick in ticks"
          :key="tick.x"
          :x="tick.x"
          :y="HEIGHT - 4"
          :text-anchor="tick.x < 12 ? 'start' : tick.x > width - 12 ? 'end' : 'middle'"
          class="fill-(--ui-text-dimmed) text-[10px]"
        >{{ tick.label }}</text>
      </svg>

      <div
        v-if="hoveredDot && hovered !== null"
        class="pointer-events-none absolute z-10 w-max -translate-x-1/2 -translate-y-full"
        :style="{ left: `${Math.min(Math.max(hoveredDot.x, 64), width - 64)}px`, top: `${hoveredDot.y - 8}px` }"
      >
        <ProfileRankChartTooltip :entry="points[hovered]!.entry" />
      </div>
    </div>
  </div>
</template>
