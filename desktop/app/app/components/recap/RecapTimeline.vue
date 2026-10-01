<script setup lang="ts">
import type { Moment } from '~/types/recordings'
import { formatClock, isPlayerMoment, momentLabel, momentMark, momentTone } from '~/utils/recording-moments'

/**
 * The recap's timeline (#1777): the video's length with the player's kills,
 * deaths and assists on the track and the game's objectives above it, the
 * playhead, and the ranges being cut. A click seeks, a click on a moment seeks
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
// Drawn assists first and kills last, so a kill is never hidden under a lesser mark.
const LAYER = { assist: 0, death: 1, kill: 2 } as Record<string, number>
const playerMoments = computed(() => visible.value.filter(isPlayerMoment).sort((a, b) => (LAYER[a.kind] ?? 0) - (LAYER[b.kind] ?? 0)))
const objectives = computed(() => visible.value.filter(moment => !isPlayerMoment(moment)))

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
    <div
      ref="track"
      class="relative h-[60px] touch-none"
      :class="editable ? 'cursor-crosshair' : 'cursor-pointer'"
      @pointerdown="onDown"
      @pointermove="onMove"
      @pointerup="onUp"
      @pointercancel="drag = null"
      @pointerleave="hoverMs = null"
    >
      <!-- Objectives, above the track: the side that took one is its colour. -->
      <button
        v-for="(moment, index) in objectives"
        :key="`objective-${index}`"
        type="button"
        class="absolute top-0 flex size-5 -translate-x-1/2 items-center justify-center rounded transition-transform hover:scale-125"
        :class="momentTone(moment)"
        :style="{ left: pct(moment.videoMs) }"
        :title="`${momentLabel(moment)} · ${formatClock(moment.videoMs)}`"
        :aria-label="`${momentLabel(moment)} at ${formatClock(moment.videoMs)}`"
        @pointerdown.stop
        @click="onMoment(moment)"
      >
        <RecapMomentIcon :kind="moment.kind" class="size-3.5" />
      </button>

      <div class="absolute inset-x-0 top-6 h-9 overflow-hidden rounded-md bg-accented ring-1 ring-default">
        <div class="absolute inset-y-0 left-0 bg-white/[0.04]" :style="{ width: pct(currentMs) }" />
        <div
          v-for="clip in saved"
          :key="clip.id"
          class="absolute bottom-0 h-1 rounded-full bg-gold/70"
          :style="{ left: pct(clip.startMs), width: `calc(${pct(clip.endMs)} - ${pct(clip.startMs)})` }"
          :title="`Saved: ${clip.title}`"
        />
      </div>

      <!-- Ranges being cut: a band with a handle at each end. -->
      <div
        v-for="range in drafts"
        :key="range.key"
        :data-range="range.key"
        class="absolute top-6 h-9 rounded-md ring-1 transition-colors"
        :class="range.key === selected ? 'bg-primary/25 ring-primary' : 'bg-primary/10 ring-primary/40 hover:bg-primary/15'"
        :style="{ left: pct(range.startMs), width: `calc(${pct(range.endMs)} - ${pct(range.startMs)})` }"
      >
        <template v-if="editable">
          <span data-handle="start" :data-key="range.key" class="absolute -left-1 inset-y-0 w-2 cursor-ew-resize rounded-sm bg-primary" :class="range.key !== selected && 'opacity-50'" />
          <span data-handle="end" :data-key="range.key" class="absolute -right-1 inset-y-0 w-2 cursor-ew-resize rounded-sm bg-primary" :class="range.key !== selected && 'opacity-50'" />
        </template>
      </div>
      <div v-if="ghost" class="pointer-events-none absolute top-6 h-9 rounded-md bg-primary/20 ring-1 ring-primary" :style="{ left: pct(ghost.startMs), width: `calc(${pct(ghost.endMs)} - ${pct(ghost.startMs)})` }" />
      <div v-if="pendingIn !== null" class="pointer-events-none absolute top-5 h-11 border-l-2 border-dashed border-primary" :style="{ left: pct(pendingIn) }" title="In point" />

      <!-- The player's own moments, on the track. -->
      <button
        v-for="(moment, index) in playerMoments"
        :key="`player-${index}`"
        type="button"
        class="absolute top-[42px] flex -translate-1/2 items-center justify-center rounded-full text-[9px] font-bold ring-2 transition-transform hover:scale-125"
        :class="[momentMark(moment), moment.kind === 'death' ? '' : 'ring-ink-950', moment.kills > 1 && 'w-auto min-w-4 px-1']"
        :style="{ left: pct(moment.videoMs) }"
        :title="`${momentLabel(moment)} · ${formatClock(moment.videoMs)}`"
        :aria-label="`${momentLabel(moment)} at ${formatClock(moment.videoMs)}`"
        @pointerdown.stop
        @click="onMoment(moment)"
      >
        <template v-if="moment.kills > 1">{{ moment.kills }}</template>
        <RecapMomentIcon v-else-if="moment.kind !== 'assist'" :kind="moment.kind" class="size-2.5" />
      </button>

      <div data-playhead class="absolute top-4 h-12 w-0.5 -translate-x-1/2 cursor-ew-resize bg-highlighted" :style="{ left: pct(currentMs) }">
        <span class="absolute -left-[5px] -top-1 size-3 rounded-full bg-highlighted ring-2 ring-ink-950" />
      </div>

    </div>

    <div class="relative mt-1 h-3 text-[10px] tabular-nums text-dimmed">
      <span v-for="at in ticks" :key="at" class="absolute -translate-x-1/2" :class="hoverMs !== null && 'opacity-40'" :style="{ left: pct(at) }">{{ formatClock(at) }}</span>
      <span
        v-if="hoverMs !== null"
        class="pointer-events-none absolute -translate-x-1/2 rounded bg-ink-950 px-1 text-highlighted"
        :style="{ left: pct(hoverMs) }"
      >{{ formatClock(hoverMs) }}</span>
    </div>
  </div>
</template>
