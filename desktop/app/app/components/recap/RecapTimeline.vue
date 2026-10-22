<script setup lang="ts">
import type { Moment } from '~/types/recordings'
import type { MomentCast } from '~/utils/recording-moments'
import type { RecapWinProbability } from '~/utils/recording-win-probability'
import { isMarked } from '~/utils/recording-moments'
import {
  TIMELINE_BARE_HEIGHT as BARE_HEIGHT,
  TIMELINE_CURVE_HEIGHT as CURVE_HEIGHT,
  clusterMoments,
  clusterNear,
  EVENT_CLUSTER_PX,
  TIMELINE_PANEL_TOP as PANEL_TOP,
  viewTicks,
} from '~/utils/recording-timeline'

/**
 * The recap's timeline (#1777): the video's length, the game's events as dots
 * above it — blue the player's side's way, red the enemy's, rose gold a
 * moment the player marked with the mark key (#2079), which also draws a line
 * across the panel; listed in a tooltip when the pointer passes over them
 * anywhere on the track (#2171) — the playhead, and the ranges being cut. A
 * click seeks, a click on an event's dot seeks a few seconds before it (to
 * the start of a marked moment's 30 s). Under the dots, when the game has
 * one, the win-probability curve (#1911), its turning points seeking like
 * events. When `editable`, a drag across the track draws a new
 * range and a range's handles move its ends. Read-only (a clip's player), a
 * drag scrubs.
 *
 * It zooms like a video editor's timeline: a pinch or Ctrl/⌘/Alt + wheel zooms
 * around the pointer, − / = / \ zoom out, in around the playhead and fit the
 * video back, and the zoom bar under the clock pans and resizes the window.
 * Zoomed in, a wheel pans, the view pages to follow the playhead, and a drag
 * past an edge scrolls it.
 */
interface Range { key: string, startMs: number, endMs: number }

const props = withDefaults(defineProps<{
  lengthMs: number
  currentMs: number
  moments: Moment[]
  editable?: boolean
  drafts?: Range[]
  selected?: string | null
  saved?: { id: string, startMs: number, endMs: number, title: string }[]
  pendingIn?: number | null
  winProbability?: RecapWinProbability | null
  /** The game's champions, to draw who killed whom in the events' tooltip. */
  cast?: MomentCast
}>(), { editable: false, drafts: () => [], selected: null, saved: () => [], pendingIn: null, winProbability: null, cast: () => ({ playerChampionId: null, champions: new Map() }) })

const emit = defineEmits<{
  seek: [ms: number]
  /** A moment or a turning point: the player lands a few seconds before it. */
  moment: [moment: Pick<Moment, 'videoMs'>]
  create: [startMs: number, endMs: number]
  resize: [key: string, startMs: number, endMs: number]
  /** `null`: a click on the bare track, which lets go of the selected range. */
  select: [key: string | null]
}>()

const track = ref<HTMLElement | null>(null)

const { view, zoomed, pct, inView, msAt, setView, zoomStep, onWheel, scrollPastEdge } = useTimelineZoom({
  lengthMs: () => props.lengthMs,
  currentMs: () => props.currentMs,
  track,
  dragging: () => drag.value !== null,
})

/** The track's width, px: how many ms one dot covers at the current zoom. */
const trackWidth = ref(0)
let resizeObserver: ResizeObserver | null = null
onMounted(() => {
  // Read now too: an observer reports only once the page renders, which a hidden window never does.
  trackWidth.value = track.value?.getBoundingClientRect().width ?? 0
  resizeObserver = new ResizeObserver(([entry]) => { trackWidth.value = entry?.contentRect.width ?? 0 })
  if (track.value) resizeObserver.observe(track.value)
})
onBeforeUnmount(() => resizeObserver?.disconnect())
const msPerPx = computed(() => (trackWidth.value ? view.value.spanMs / trackWidth.value : 0))

const visible = computed(() => props.moments.filter(moment => moment.videoMs <= props.lengthMs && inView(moment.videoMs)))
const clusters = computed(() => clusterMoments(visible.value, EVENT_CLUSTER_PX * msPerPx.value))
const marks = computed(() => visible.value.filter(isMarked))

/** The curve's lane when the game has one, a bare strip otherwise. */
const panelHeight = computed(() => (props.winProbability ? CURVE_HEIGHT : BARE_HEIGHT))
const trackHeight = computed(() => `${PANEL_TOP + panelHeight.value}px`)

// Clock labels: the smallest step that keeps about eight across the window.
const ticks = computed(() => viewTicks(view.value))

type Drag =
  | { mode: 'press', x: number, ms: number, range: string | null }
  | { mode: 'create', anchor: number, ms: number }
  | { mode: 'resize', key: string, edge: 'start' | 'end' }
  | { mode: 'scrub' }

const drag = ref<Drag | null>(null)
const hoverMs = ref<number | null>(null)
/** The events under the pointer, wherever it is on the track: its column, a dot's width either side. */
const hovered = computed(() => (hoverMs.value === null || drag.value
  ? null
  : clusterNear(clusters.value, hoverMs.value, EVENT_CLUSTER_PX * msPerPx.value)))
const ghost = computed(() => (drag.value?.mode === 'create'
  ? { startMs: Math.min(drag.value.anchor, drag.value.ms), endMs: Math.max(drag.value.anchor, drag.value.ms) }
  : null))

function onDown(event: PointerEvent) {
  if (event.button !== 0) return
  const target = event.target as HTMLElement
  const handle = target.closest<HTMLElement>('[data-handle]')
  const range = target.closest<HTMLElement>('[data-range]')
  ;(event.currentTarget as HTMLElement).setPointerCapture(event.pointerId)
  if (handle && props.editable) {
    drag.value = { mode: 'resize', key: handle.dataset.key!, edge: handle.dataset.handle as 'start' | 'end' }
    emit('select', handle.dataset.key!)
  }
  else if (!props.editable || target.closest('[data-playhead]')) {
    drag.value = { mode: 'scrub' }
    emit('seek', msAt(event.clientX))
  }
  else {
    drag.value = { mode: 'press', x: event.clientX, ms: msAt(event.clientX), range: range?.dataset.range ?? null }
  }
}

function onMove(event: PointerEvent) {
  hoverMs.value = msAt(event.clientX)
  const current = drag.value
  if (!current) return
  if (current.mode !== 'press') scrollPastEdge(event.clientX)
  const ms = msAt(event.clientX)
  if (current.mode === 'press' && Math.abs(event.clientX - current.x) > 4) drag.value = { mode: 'create', anchor: current.ms, ms }
  else if (current.mode === 'create') drag.value = { ...current, ms }
  else if (current.mode === 'scrub') emit('seek', ms)
  else if (current.mode === 'resize') {
    const range = props.drafts.find(entry => entry.key === current.key)
    if (!range) return
    if (current.edge === 'start') emit('resize', range.key, Math.min(ms, range.endMs), range.endMs)
    else emit('resize', range.key, range.startMs, Math.max(ms, range.startMs))
  }
}

function onUp(event: PointerEvent) {
  const current = drag.value
  drag.value = null
  if (!current) return
  if (current.mode === 'create') emit('create', Math.min(current.anchor, current.ms), Math.max(current.anchor, current.ms))
  else if (current.mode === 'press') {
    if (props.editable) emit('select', current.range)
    emit('seek', msAt(event.clientX))
  }
}

function onMoment(moment: Pick<Moment, 'videoMs'>) {
  emit('moment', moment)
}
</script>

<template>
  <div class="select-none">
    <div class="flex gap-2">
      <!-- The lanes' legend. -->
      <RecapTimelineLegend :height="trackHeight" :curve="!!winProbability" />

      <div
        ref="track"
        class="relative min-w-0 flex-1 touch-none"
        :class="[editable ? 'cursor-crosshair' : 'cursor-pointer', zoomed && 'overflow-x-clip']"
        :style="{ height: trackHeight }"
        @wheel="onWheel"
        @pointerdown="onDown"
        @pointermove="onMove"
        @pointerup="onUp"
        @pointercancel="drag = null"
        @pointerleave="hoverMs = null"
      >
        <RecapTimelineEvents :clusters="clusters" :view="view" :hovered="hovered" :cast="cast" @moment="onMoment" />

        <div class="absolute inset-x-0 overflow-hidden rounded-md bg-muted ring-1 ring-default" :style="{ top: `${PANEL_TOP}px`, height: `${panelHeight}px` }">
          <div class="absolute inset-y-0 left-0 bg-white/[0.035]" :style="{ width: pct(currentMs) }" />
          <div v-for="at in ticks" :key="`grid-${at}`" class="absolute inset-y-0 w-px bg-white/[0.04]" :style="{ left: pct(at) }" />
          <div v-if="hoverMs !== null && !drag" class="absolute inset-y-0 w-px" :class="hovered ? 'bg-white/30' : 'bg-white/15'" :style="{ left: pct(hoverMs) }" />
          <div v-for="mark in marks" :key="`mark-${mark.videoMs}`" class="absolute inset-y-0 w-px bg-primary/60" :style="{ left: pct(mark.videoMs) }" />
          <div
            v-for="clip in saved"
            :key="clip.id"
            class="absolute bottom-0 h-1 rounded-full bg-gold/70"
            :style="{ left: pct(clip.startMs), width: `calc(${pct(clip.endMs)} - ${pct(clip.startMs)})` }"
            :title="`Saved: ${clip.title}`"
          />
        </div>

        <!-- Ranges being cut: a band across every lane with a handle at each end. -->
        <div
          v-for="range in drafts"
          :key="range.key"
          :data-range="range.key"
          class="absolute rounded-md ring-1 transition-colors"
          :class="range.key === selected ? 'bg-primary/20 ring-primary' : 'bg-primary/10 ring-primary/40 hover:bg-primary/15'"
          :style="{ top: `${PANEL_TOP}px`, height: `${panelHeight}px`, left: pct(range.startMs), width: `calc(${pct(range.endMs)} - ${pct(range.startMs)})` }"
        >
          <template v-if="editable">
            <span v-if="inView(range.startMs)" data-handle="start" :data-key="range.key" class="absolute -left-1 inset-y-0 w-2 cursor-ew-resize rounded-sm bg-primary" :class="range.key !== selected && 'opacity-50'" />
            <span v-if="inView(range.endMs)" data-handle="end" :data-key="range.key" class="absolute -right-1 inset-y-0 w-2 cursor-ew-resize rounded-sm bg-primary" :class="range.key !== selected && 'opacity-50'" />
          </template>
        </div>
        <div v-if="ghost" class="pointer-events-none absolute rounded-md bg-primary/20 ring-1 ring-primary" :style="{ top: `${PANEL_TOP}px`, height: `${panelHeight}px`, left: pct(ghost.startMs), width: `calc(${pct(ghost.endMs)} - ${pct(ghost.startMs)})` }" />
        <div v-if="pendingIn !== null && inView(pendingIn)" class="pointer-events-none absolute bottom-0 border-l-2 border-dashed border-primary" :style="{ top: `${PANEL_TOP - 4}px`, left: pct(pendingIn) }" title="In point" />

        <RecapWinProbabilityLane
          v-if="winProbability"
          class="absolute inset-x-0"
          :style="{ top: `${PANEL_TOP}px`, height: `${CURVE_HEIGHT}px` }"
          :curve="winProbability.curve"
          :turning-points="winProbability.turningPoints"
          :length-ms="lengthMs"
          :view="view"
          :height="CURVE_HEIGHT"
          @jump="onMoment"
        />

        <div v-if="inView(currentMs)" data-playhead class="absolute bottom-0 z-20 w-0.5 -translate-x-1/2 cursor-ew-resize bg-inverted" :style="{ top: `${PANEL_TOP - 6}px`, left: pct(currentMs) }">
          <span class="absolute -left-[5px] -top-1.5 size-3 rounded-full bg-inverted ring-2 ring-(--ui-bg)" />
        </div>
      </div>
    </div>

    <RecapTimelineClock :ticks="ticks" :view="view" :hover-ms="hoverMs" />

    <RecapTimelineNavigator class="mt-1.5" :view="view" :length-ms="lengthMs" :current-ms="currentMs" @update:view="setView" @zoom="zoomStep" />
  </div>
</template>
