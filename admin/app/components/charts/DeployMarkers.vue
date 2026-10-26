<script setup lang="ts">
// Deploy markers (#2297): a thin vertical rule and a version label over a chart, one per
// marker, drawn by <ChartsAreaChart> and <ChartsBarChart> when a call site passes `markers`.
//
// An OVERLAY rather than a unovis component inside the chart: vue-chrts builds its
// <VisXYContainer> children itself and exposes no slot inside it, so neither a crosshair
// annotation nor a plot line can be added from outside. The overlay instead reads the
// rendered chart back — the x-axis ticks carry their row index as d3 data and their pixel
// offset as a transform — and places each rule on the same linear scale. It re-reads on
// every transform change (the axis animates in, and re-renders on resize), throttled to one
// read per frame. If upstream ever renders no readable ticks, no marker is drawn: a missing
// marker is a gap, a misplaced one would be a lie.
//
// Labels are HTML, not SVG tick text, so they never go through unovis' XML string-building
// and need none of the `chart-text.ts` escaping; Vue interpolation escapes them.
import { onBeforeUnmount, onMounted, ref, useTemplateRef, watch } from 'vue'
import type { AxisTick, ChartMarker } from '~/utils/deploy-markers'
import { indexToPixelFromTicks, labelRows } from '~/utils/deploy-markers'

const props = defineProps<{
  markers: readonly ChartMarker[]
}>()

interface PlacedMarker extends ChartMarker {
  x: number
  row: number
  /** Near the right edge the label sits left of its rule, so it is not clipped. */
  flip: boolean
}

/** Room a label needs to the right of its rule. */
const LABEL_ROOM_PX = 72

const overlay = useTemplateRef<HTMLElement>('overlay')
const placed = ref<PlacedMarker[]>([])
const plot = ref({ top: 0, height: 0 })

const TRANSLATE = /translate\(\s*(-?[\d.e+-]+)[\s,]+(-?[\d.e+-]+)\s*\)/

function translateOf(element: Element): [number, number] | null {
  const match = TRANSLATE.exec(element.getAttribute('transform') ?? '')
  return match ? [Number(match[1]), Number(match[2])] : null
}

/** The x axis: the axis group whose ticks move along x. */
function readXAxis(svg: SVGSVGElement) {
  for (const axis of svg.querySelectorAll('g[class*="axis-component"]')) {
    const ticks: AxisTick[] = []
    let movesAlongX = false
    for (const tick of axis.querySelectorAll('g.tick')) {
      const offset = translateOf(tick)
      const value = (tick as Element & { __data__?: unknown }).__data__
      if (!offset || typeof value !== 'number') {
        continue
      }
      movesAlongX ||= offset[0] !== 0
      ticks.push({ value, position: offset[0] })
    }
    const origin = translateOf(axis)
    if (movesAlongX && origin && ticks.length >= 2) {
      return { ticks, origin }
    }
  }
  return null
}

function measure() {
  const host = overlay.value?.parentElement
  const svg = host
    ? [...host.querySelectorAll('svg')].find(candidate => candidate.querySelector('g[class*="axis-component"]'))
    : undefined
  const axis = svg ? readXAxis(svg) : null
  const toPixel = axis ? indexToPixelFromTicks(axis.ticks) : null
  if (!host || !svg || !axis || !toPixel || props.markers.length === 0) {
    placed.value = []
    return
  }
  const svgBox = svg.getBoundingClientRect()
  const hostBox = host.getBoundingClientRect()
  const left = svgBox.left - hostBox.left + axis.origin[0]
  const top = svgBox.top - hostBox.top
  plot.value = { top, height: Math.max(0, axis.origin[1]) }

  const xs = props.markers.map(marker => left + toPixel(marker.index))
  const rows = labelRows(xs)
  placed.value = props.markers.map((marker, i) => ({
    ...marker,
    x: xs[i]!,
    row: rows[i]!,
    flip: xs[i]! > hostBox.width - LABEL_ROOM_PX,
  }))
}

let frame: number | null = null
function scheduleMeasure() {
  if (frame !== null) {
    return
  }
  frame = requestAnimationFrame(() => {
    frame = null
    measure()
  })
}

let mutations: MutationObserver | null = null
let resizes: ResizeObserver | null = null
onMounted(() => {
  const host = overlay.value?.parentElement
  if (!host) {
    return
  }
  mutations = new MutationObserver(scheduleMeasure)
  mutations.observe(host, { subtree: true, childList: true, attributes: true, attributeFilter: ['transform'] })
  resizes = new ResizeObserver(scheduleMeasure)
  resizes.observe(host)
  scheduleMeasure()
})
watch(() => props.markers, scheduleMeasure, { deep: true })
onBeforeUnmount(() => {
  mutations?.disconnect()
  resizes?.disconnect()
  if (frame !== null) {
    cancelAnimationFrame(frame)
  }
})
</script>

<template>
  <div
    ref="overlay"
    class="pointer-events-none absolute inset-0 overflow-hidden"
    data-testid="deploy-markers"
  >
    <div
      v-for="marker in placed"
      :key="marker.index"
      class="absolute border-l border-dashed border-zinc-500/70"
      :style="{ left: `${marker.x}px`, top: `${plot.top}px`, height: `${plot.height}px` }"
    >
      <span
        class="pointer-events-auto absolute whitespace-nowrap rounded bg-default/80 px-1 text-[10px] leading-4 text-muted tabular-nums"
        :class="marker.flip ? 'right-1' : 'left-1'"
        :style="{ top: `${marker.row * 16}px` }"
        :title="marker.title"
      >{{ marker.label }}</span>
    </div>
  </div>
</template>
