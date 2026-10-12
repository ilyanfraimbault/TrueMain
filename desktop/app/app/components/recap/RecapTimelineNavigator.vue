<script setup lang="ts">
import type { TimelineView } from '~/utils/recording-timeline'
import { clampView, fullView, isZoomed, MIN_VIEW_SPAN_MS } from '~/utils/recording-timeline'

/**
 * The timeline's zoom bar, after a video editor's: the whole video across, the
 * window the timeline shows as a thumb on it. Dragging the thumb pans, dragging
 * one of its ends zooms on that side, a click beside it centres the window
 * there and a double click fits the whole video back. The buttons zoom out, in
 * and fit, the same steps as − / = / \ on the keyboard.
 */
const props = defineProps<{
  view: TimelineView
  lengthMs: number
  currentMs: number
}>()

const emit = defineEmits<{
  'update:view': [view: TimelineView]
  'zoom': [direction: 1 | -1]
}>()

/** The thumb's narrowest, px: a few seconds of a long game would otherwise be a hairline. */
const MIN_THUMB_PX = 16

const bar = ref<HTMLElement | null>(null)
const length = computed(() => Math.max(props.lengthMs, 1))
const zoomed = computed(() => isZoomed(props.view, props.lengthMs))
const pct = (ms: number) => `${(Math.min(Math.max(ms, 0), length.value) / length.value) * 100}%`
const narrowest = computed(() => props.view.spanMs <= Math.min(MIN_VIEW_SPAN_MS, length.value))

type Drag =
  | { mode: 'pan', x: number, startMs: number }
  | { mode: 'start' | 'end' }

const drag = ref<Drag | null>(null)

function msAt(clientX: number): number {
  const rect = bar.value?.getBoundingClientRect()
  if (!rect || !rect.width) return 0
  return Math.min(Math.max((clientX - rect.left) / rect.width, 0), 1) * length.value
}

function onDown(event: PointerEvent) {
  if (event.button !== 0) return
  const target = event.target as HTMLElement
  ;(event.currentTarget as HTMLElement).setPointerCapture(event.pointerId)
  const edge = target.closest<HTMLElement>('[data-edge]')?.dataset.edge as 'start' | 'end' | undefined
  if (edge) drag.value = { mode: edge }
  else if (target.closest('[data-thumb]')) drag.value = { mode: 'pan', x: event.clientX, startMs: props.view.startMs }
  else {
    emit('update:view', clampView({ startMs: msAt(event.clientX) - props.view.spanMs / 2, spanMs: props.view.spanMs }, props.lengthMs))
    drag.value = { mode: 'pan', x: event.clientX, startMs: msAt(event.clientX) - props.view.spanMs / 2 }
  }
}

function onMove(event: PointerEvent) {
  const current = drag.value
  const rect = bar.value?.getBoundingClientRect()
  if (!current || !rect?.width) return
  const end = props.view.startMs + props.view.spanMs
  if (current.mode === 'pan') {
    const deltaMs = ((event.clientX - current.x) / rect.width) * length.value
    emit('update:view', clampView({ startMs: current.startMs + deltaMs, spanMs: props.view.spanMs }, props.lengthMs))
  }
  else if (current.mode === 'start') {
    const startMs = Math.min(msAt(event.clientX), end - MIN_VIEW_SPAN_MS)
    emit('update:view', clampView({ startMs, spanMs: end - startMs }, props.lengthMs))
  }
  else {
    const endMs = Math.max(msAt(event.clientX), props.view.startMs + MIN_VIEW_SPAN_MS)
    emit('update:view', clampView({ startMs: props.view.startMs, spanMs: endMs - props.view.startMs }, props.lengthMs))
  }
}

const fit = () => emit('update:view', fullView(props.lengthMs))
</script>

<template>
  <div class="ml-6 flex items-center gap-1.5">
    <UButton icon="i-lucide-zoom-out" color="neutral" variant="ghost" size="xs" aria-label="Zoom out" title="Zoom out (−)" :disabled="!zoomed" @click="emit('zoom', -1)" />
    <div
      ref="bar"
      class="relative h-2.5 min-w-0 flex-1 cursor-pointer touch-none rounded-full bg-muted ring-1 ring-default"
      @pointerdown="onDown"
      @pointermove="onMove"
      @pointerup="drag = null"
      @pointercancel="drag = null"
    >
      <div class="pointer-events-none absolute inset-y-0 w-px bg-ink-400" :style="{ left: pct(currentMs) }" />
      <div
        data-thumb
        class="absolute inset-y-0 rounded-full bg-white/15 ring-1 ring-white/25 transition-colors hover:bg-white/20"
        :class="drag?.mode === 'pan' ? 'cursor-grabbing bg-white/25' : 'cursor-grab'"
        :style="{ left: `min(${pct(view.startMs)}, calc(100% - ${MIN_THUMB_PX}px))`, width: `max(${MIN_THUMB_PX}px, calc(${pct(view.startMs + view.spanMs)} - ${pct(view.startMs)}))` }"
        title="Drag to pan, drag an end to zoom, double-click to fit"
        @dblclick="fit"
      >
        <!-- Just outside the thumb, so even at its narrowest its body still pans. -->
        <span data-edge="start" class="absolute right-full top-1/2 size-2.5 translate-x-0.5 -translate-y-1/2 cursor-ew-resize rounded-full bg-ink-200 ring-2 ring-(--ui-bg)" />
        <span data-edge="end" class="absolute left-full top-1/2 size-2.5 -translate-x-0.5 -translate-y-1/2 cursor-ew-resize rounded-full bg-ink-200 ring-2 ring-(--ui-bg)" />
      </div>
    </div>
    <UButton icon="i-lucide-zoom-in" color="neutral" variant="ghost" size="xs" aria-label="Zoom in" title="Zoom in (=)" :disabled="narrowest" @click="emit('zoom', 1)" />
    <UButton icon="i-lucide-unfold-horizontal" color="neutral" variant="ghost" size="xs" aria-label="Fit the whole video" title="Fit the whole video (\)" :disabled="!zoomed" @click="fit" />
  </div>
</template>
