<script setup lang="ts">
import type { Moment } from '~/types/recordings'
import type { PlayerLane } from '~/utils/recording-moments'
import { formatClock, isPlayerMoment, isStructure, LANE_NAMES, momentLabel, momentMark, momentTone, PLAYER_LANES } from '~/utils/recording-moments'

/**
 * The recap's timeline (#1777): the video's length with the player's kills,
 * assists and deaths each on a lane of their own — named by the glyph beside
 * it — and the game's objectives above them, the playhead, and the ranges
 * being cut. A click seeks, a click on a moment seeks
 * a few seconds before it; when `editable`, a drag across the track draws a new
 * range and a range's handles move its ends. Read-only (a clip's player), a
 * drag scrubs.
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
}>(), { editable: false, drafts: () => [], selected: null, saved: () => [], pendingIn: null })

const emit = defineEmits<{
  seek: [ms: number]
  moment: [moment: Moment]
  create: [startMs: number, endMs: number]
  resize: [key: string, startMs: number, endMs: number]
  /** `null`: a click on the bare track, which lets go of the selected range. */
  select: [key: string | null]
}>()

const track = ref<HTMLElement | null>(null)
const pct = (ms: number) => `${(Math.min(Math.max(ms, 0), props.lengthMs) / Math.max(props.lengthMs, 1)) * 100}%`

function msAt(clientX: number): number {
  const rect = track.value?.getBoundingClientRect()
  if (!rect || !rect.width) return 0
  return Math.min(Math.max((clientX - rect.left) / rect.width, 0), 1) * props.lengthMs
}

const visible = computed(() => props.moments.filter(moment => moment.videoMs <= props.lengthMs))
const lanes = computed(() => PLAYER_LANES.map(lane => ({
  lane,
  // Bigger multi-kills drawn last, so a penta is never hidden under a single kill.
  moments: visible.value.filter(moment => moment.kind === lane).sort((a, b) => a.kills - b.kills),
})))
// Structures first, so an epic monster's badge is drawn over a tower that fell at the same time.
const objectives = computed(() => visible.value.filter(moment => !isPlayerMoment(moment))
  .sort((a, b) => Number(!isStructure(a)) - Number(!isStructure(b))))

/** Geometry, px: the objectives' row, then the lanes' panel under it. */
const PANEL_TOP = 28
const LANE_HEIGHT = 20
const laneCenter = (lane: PlayerLane) => PANEL_TOP + PLAYER_LANES.indexOf(lane) * LANE_HEIGHT + LANE_HEIGHT / 2
const LANE_TONES: Record<PlayerLane, string> = { kill: 'text-data-good', assist: 'text-ink-400', death: 'text-red-400' }

// Minute labels: the smallest step that keeps them to about eight.
const ticks = computed(() => {
  const steps = [10_000, 30_000, 60_000, 120_000, 300_000, 600_000, 900_000]
  const step = steps.find(value => props.lengthMs / value <= 8) ?? 1_800_000
  const list: number[] = []
  for (let at = step; at < props.lengthMs - step / 3; at += step) list.push(at)
  return list
})

type Drag =
  | { mode: 'press', x: number, ms: number, range: string | null }
  | { mode: 'create', anchor: number, ms: number }
  | { mode: 'resize', key: string, edge: 'start' | 'end' }
  | { mode: 'scrub' }

const drag = ref<Drag | null>(null)
const hoverMs = ref<number | null>(null)
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

function onMoment(moment: Moment) {
  emit('moment', moment)
}
</script>

<template>
  <div class="select-none">
    <div class="flex gap-2">
      <!-- The lanes' legend. -->
      <div class="relative h-[88px] w-4 shrink-0">
        <div
          v-for="lane in PLAYER_LANES"
          :key="lane"
          class="absolute inset-x-0 flex items-center justify-center"
          :style="{ top: `${laneCenter(lane) - LANE_HEIGHT / 2}px`, height: `${LANE_HEIGHT}px` }"
          :title="LANE_NAMES[lane]"
        >
          <RecapMomentIcon :kind="lane" class="size-3.5" :class="LANE_TONES[lane]" />
        </div>
      </div>

      <div
        ref="track"
        class="relative h-[88px] min-w-0 flex-1 touch-none"
        :class="editable ? 'cursor-crosshair' : 'cursor-pointer'"
        @pointerdown="onDown"
        @pointermove="onMove"
        @pointerup="onUp"
        @pointercancel="drag = null"
        @pointerleave="hoverMs = null"
      >
        <!-- Objectives, above the lanes: the side that took one is its colour. -->
        <button
          v-for="(moment, index) in objectives"
          :key="`objective-${index}`"
          type="button"
          class="absolute top-0.5 flex -translate-x-1/2 items-center justify-center rounded-md transition-transform hover:z-10 hover:scale-125"
          :class="[
            momentTone(moment),
            isStructure(moment)
              ? 'size-5 opacity-75 hover:opacity-100'
              : ['size-5 ring-1', moment.ally === false ? 'bg-enemy/15 ring-enemy/50' : 'bg-ally/15 ring-ally/50'],
          ]"
          :style="{ left: pct(moment.videoMs) }"
          :title="`${momentLabel(moment)} · ${formatClock(moment.videoMs)}`"
          :aria-label="`${momentLabel(moment)} at ${formatClock(moment.videoMs)}`"
          @pointerdown.stop
          @click="onMoment(moment)"
        >
          <RecapMomentIcon :kind="moment.kind" :class="isStructure(moment) ? 'size-3' : 'size-3.5'" />
        </button>

        <div class="absolute inset-x-0 overflow-hidden rounded-md bg-muted ring-1 ring-default" :style="{ top: `${PANEL_TOP}px`, height: `${LANE_HEIGHT * PLAYER_LANES.length}px` }">
          <div class="absolute inset-y-0 left-0 bg-white/[0.035]" :style="{ width: pct(currentMs) }" />
          <div v-for="at in ticks" :key="`grid-${at}`" class="absolute inset-y-0 w-px bg-white/[0.04]" :style="{ left: pct(at) }" />
          <div v-for="index in PLAYER_LANES.length - 1" :key="`lane-${index}`" class="absolute inset-x-0 h-px bg-white/[0.05]" :style="{ top: `${index * LANE_HEIGHT}px` }" />
          <div v-if="hoverMs !== null && !drag" class="absolute inset-y-0 w-px bg-white/15" :style="{ left: pct(hoverMs) }" />
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
          :style="{ top: `${PANEL_TOP}px`, height: `${LANE_HEIGHT * PLAYER_LANES.length}px`, left: pct(range.startMs), width: `calc(${pct(range.endMs)} - ${pct(range.startMs)})` }"
        >
          <template v-if="editable">
            <span data-handle="start" :data-key="range.key" class="absolute -left-1 inset-y-0 w-2 cursor-ew-resize rounded-sm bg-primary" :class="range.key !== selected && 'opacity-50'" />
            <span data-handle="end" :data-key="range.key" class="absolute -right-1 inset-y-0 w-2 cursor-ew-resize rounded-sm bg-primary" :class="range.key !== selected && 'opacity-50'" />
          </template>
        </div>
        <div v-if="ghost" class="pointer-events-none absolute rounded-md bg-primary/20 ring-1 ring-primary" :style="{ top: `${PANEL_TOP}px`, height: `${LANE_HEIGHT * PLAYER_LANES.length}px`, left: pct(ghost.startMs), width: `calc(${pct(ghost.endMs)} - ${pct(ghost.startMs)})` }" />
        <div v-if="pendingIn !== null" class="pointer-events-none absolute bottom-0 border-l-2 border-dashed border-primary" :style="{ top: `${PANEL_TOP - 4}px`, left: pct(pendingIn) }" title="In point" />

        <!-- The player's own moments, each kind on its lane. -->
        <template v-for="{ lane, moments: laneMoments } in lanes" :key="lane">
          <button
            v-for="(moment, index) in laneMoments"
            :key="`${lane}-${index}`"
            type="button"
            class="absolute flex -translate-1/2 items-center justify-center rounded-full text-[9px] font-bold leading-none transition-transform hover:z-10 hover:scale-125"
            :class="[momentMark(moment), moment.kills > 1 && 'px-1', lane !== 'death' && 'ring-2 ring-(--ui-bg-muted)']"
            :style="{ left: pct(moment.videoMs), top: `${laneCenter(lane)}px` }"
            :title="`${momentLabel(moment)} · ${formatClock(moment.videoMs)}`"
            :aria-label="`${momentLabel(moment)} at ${formatClock(moment.videoMs)}`"
            @pointerdown.stop
            @click="onMoment(moment)"
          >
            <template v-if="moment.kills > 1">×{{ moment.kills }}</template>
            <RecapMomentIcon v-else-if="lane !== 'assist'" :kind="moment.kind" class="size-2.5" />
          </button>
        </template>

        <div data-playhead class="absolute bottom-0 z-20 w-0.5 -translate-x-1/2 cursor-ew-resize bg-inverted" :style="{ top: `${PANEL_TOP - 6}px`, left: pct(currentMs) }">
          <span class="absolute -left-[5px] -top-1.5 size-3 rounded-full bg-inverted ring-2 ring-(--ui-bg)" />
        </div>
      </div>
    </div>

    <div class="relative ml-6 mt-1 h-3 text-[10px] tabular-nums text-dimmed">
      <span v-for="at in ticks" :key="at" class="absolute -translate-x-1/2" :class="hoverMs !== null && 'opacity-40'" :style="{ left: pct(at) }">{{ formatClock(at) }}</span>
      <span
        v-if="hoverMs !== null"
        class="pointer-events-none absolute -translate-x-1/2 rounded bg-ink-950 px-1 text-highlighted"
        :style="{ left: pct(hoverMs) }"
      >{{ formatClock(hoverMs) }}</span>
    </div>
  </div>
</template>
