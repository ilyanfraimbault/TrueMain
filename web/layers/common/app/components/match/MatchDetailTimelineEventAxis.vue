<script setup lang="ts">
import type { AxisCluster, AxisEvent } from '#common/utils/timeline-events'
import { AXIS_CLUSTER_PX, axisRows, clusterAxisEvents, clusterNear } from '#common/utils/timeline-events'
import { formatGameClock } from '#common/utils/win-probability-swing'

/**
 * Under the gold / XP / CS curves and their lead band: the game's kills and
 * objectives as dots on a time axis, drawn after the desktop recap's timeline
 * (#2171) — blue above the line for the viewer's side, red below it for the
 * enemy's. Dots closer than a dot's width at the rendered size share one, and
 * the pointer anywhere along a side's row (or the keyboard's focus on a dot)
 * opens the tooltip listing what that dot holds. Then the minute labels.
 */
const props = defineProps<{
  events: AxisEvent[]
  /** The game's length, ms: the axis' width. */
  durationMs: number
  minutes: { minute: number, x: number }[]
  champion: (participantId: number) => { name: string, iconUrl: string | null }
}>()

/** The track's width, px: how many ms one dot covers at the current size. */
const track = ref<HTMLElement | null>(null)
const trackWidth = ref(0)
let resizeObserver: ResizeObserver | null = null
onMounted(() => {
  trackWidth.value = track.value?.getBoundingClientRect().width ?? 0
  if (typeof ResizeObserver === 'undefined' || !track.value) return
  resizeObserver = new ResizeObserver(([entry]) => { trackWidth.value = entry?.contentRect.width ?? 0 })
  resizeObserver.observe(track.value)
})
onBeforeUnmount(() => resizeObserver?.disconnect())

const duration = computed(() => Math.max(1, props.durationMs))
const gapMs = computed(() => (trackWidth.value ? (AXIS_CLUSTER_PX * duration.value) / trackWidth.value : 0))
const clusters = computed(() => clusterAxisEvents(props.events, gapMs.value))
const x = (ms: number) => Math.min(100, Math.max(0, (ms / duration.value) * 100))

const pointed = ref<AxisCluster | null>(null)
const focused = ref<AxisCluster | null>(null)
const hovered = computed(() => focused.value ?? pointed.value)
const rows = computed(() => (hovered.value ? axisRows(hovered.value) : []))

/** The side's row under the pointer — the upper half ours, the lower theirs — and its dot within a dot's width. */
function onMove(event: PointerEvent) {
  const box = track.value?.getBoundingClientRect()
  if (!box || box.width === 0) return
  const ms = ((event.clientX - box.left) / box.width) * duration.value
  const ours = event.clientY < box.top + box.height / 2
  pointed.value = clusterNear(clusters.value, ours, ms, gapMs.value || duration.value / 100)
}

function describe(cluster: AxisCluster) {
  return axisRows(cluster).map((row) => {
    const what = row.type === 'objective'
      ? row.marker.label
      : `${row.killerId === null ? '' : props.champion(row.killerId).name + ' '}${row.label.toLowerCase()}: ${row.victimIds.map(id => props.champion(id).name).join(', ')}`
    return `${what} at ${formatGameClock(row.ms)}`
  }).join('; ')
}
</script>

<template>
  <div>
    <div ref="track" class="relative mt-1 h-7" @pointermove="onMove" @pointerleave="pointed = null">
      <div class="absolute inset-x-0 top-1/2 h-px bg-accented" />
      <button
        v-for="cluster in clusters"
        :key="`${cluster.ours ? 'ours' : 'theirs'}-${cluster.ms}`"
        type="button"
        class="absolute -translate-1/2 cursor-default rounded-full ring-2 ring-(--ui-bg) transition-transform focus-visible:outline-2 focus-visible:outline-primary"
        :class="[
          cluster.ours ? 'top-[7px] bg-ally' : 'top-[21px] bg-enemy',
          cluster.events.length > 1 ? 'size-3' : 'size-2.5',
          cluster === hovered && 'z-10 scale-150',
        ]"
        :style="{ left: `${x(cluster.ms)}%` }"
        :aria-label="describe(cluster)"
        @focus="focused = cluster"
        @blur="focused = null"
      />

      <MatchDetailTimelineEventTooltip
        v-if="hovered"
        :rows="rows"
        :x="x(hovered.ms)"
        :ours="hovered.ours"
        :champion="champion"
      />
    </div>
    <div class="relative h-4 text-[10px] tabular-nums text-dimmed">
      <span
        v-for="tick in minutes"
        :key="tick.minute"
        class="absolute -translate-x-1/2"
        :style="{ left: `${tick.x}%` }"
      >{{ tick.minute }}'</span>
    </div>
  </div>
</template>
