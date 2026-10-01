<script setup lang="ts">
import type { Moment } from '~/types/recordings'
import { formatClock } from '~/utils/recording-moments'

/**
 * A recording's video with the app's own controls and the timeline under it —
 * the recap of a full game (`editable`: ranges are cut on the timeline, I and O
 * set their ends at the playhead) or a saved clip's player. A jump to a moment
 * lands a few seconds before it, so the action is seen building up.
 *
 * Keys: Space plays, ← → move 5 s, Shift+← → the previous / next moment, M mutes,
 * I / O set in / out.
 */
const props = withDefaults(defineProps<{
  src: string | null
  /** The recording's length as the shell measured it — the moments are placed against it; the video's own length stands in while it is unknown. */
  lengthMs: number | null
  moments: Moment[]
  editable?: boolean
  drafts?: { key: string, startMs: number, endMs: number }[]
  selected?: string | null
  saved?: { id: string, startMs: number, endMs: number, title: string }[]
  pendingIn?: number | null
}>(), { editable: false, drafts: () => [], selected: null, saved: () => [], pendingIn: null })

const emit = defineEmits<{
  create: [startMs: number, endMs: number]
  resize: [key: string, startMs: number, endMs: number]
  select: [key: string | null]
  setIn: [ms: number]
  setOut: [ms: number]
}>()

/** How far before a moment a jump lands. */
const LEAD_MS = 5_000

const frame = ref<HTMLElement | null>(null)
const video = ref<HTMLVideoElement | null>(null)
const currentMs = ref(0)
const videoLengthMs = ref<number | null>(null)
const playing = ref(false)
const muted = ref(false)
const failed = ref(false)
let stopAt: number | null = null
let raf = 0

const length = computed(() => props.lengthMs ?? videoLengthMs.value ?? 0)
const sorted = computed(() => [...props.moments].sort((a, b) => a.videoMs - b.videoMs))
const devHint = import.meta.dev && !insideTauri()

watch(() => props.src, () => {
  failed.value = false
  videoLengthMs.value = null
  currentMs.value = 0
})

function tick() {
  const element = video.value
  if (!element) return
  currentMs.value = element.currentTime * 1000
  if (stopAt !== null && currentMs.value >= stopAt) {
    element.pause()
    stopAt = null
  }
  if (!element.paused) raf = requestAnimationFrame(tick)
}

function onPlay() {
  playing.value = true
  cancelAnimationFrame(raf)
  raf = requestAnimationFrame(tick)
}
function onPause() {
  playing.value = false
  cancelAnimationFrame(raf)
  tick()
}
function onMetadata() {
  const duration = video.value?.duration
  if (duration && Number.isFinite(duration)) videoLengthMs.value = duration * 1000
}
onBeforeUnmount(() => cancelAnimationFrame(raf))

function seek(ms: number) {
  const clamped = Math.min(Math.max(ms, 0), length.value)
  currentMs.value = clamped
  stopAt = null
  if (video.value) video.value.currentTime = clamped / 1000
}

function toggle() {
  const element = video.value
  if (!element || failed.value) return
  stopAt = null
  if (element.paused) void element.play()
  else element.pause()
}

/** Play `[startMs, endMs]` once and stop at its end — a range's preview. */
function playRange(startMs: number, endMs: number) {
  seek(startMs)
  stopAt = endMs
  void video.value?.play()
}

const jumpTo = (moment: Moment) => seek(moment.videoMs - LEAD_MS)

function nextMoment() {
  const found = sorted.value.find(moment => moment.videoMs - LEAD_MS > currentMs.value + 500)
  if (found) jumpTo(found)
}
function previousMoment() {
  const found = [...sorted.value].reverse().find(moment => moment.videoMs - LEAD_MS < currentMs.value - 1_500)
  if (found) jumpTo(found)
  else seek(0)
}

function toggleMute() {
  muted.value = !muted.value
  if (video.value) video.value.muted = muted.value
}

function fullscreen() {
  if (document.fullscreenElement) void document.exitFullscreen()
  else void frame.value?.requestFullscreen()
}

defineShortcuts({
  ' ': toggle,
  arrowleft: () => seek(currentMs.value - 5_000),
  arrowright: () => seek(currentMs.value + 5_000),
  shift_arrowleft: previousMoment,
  shift_arrowright: nextMoment,
  m: toggleMute,
  i: () => props.editable && emit('setIn', currentMs.value),
  o: () => props.editable && emit('setOut', currentMs.value),
})

defineExpose({ seek, playRange, jumpTo, currentMs })
</script>

<template>
  <div class="flex min-w-0 flex-col gap-2">
    <div ref="frame" class="relative aspect-video overflow-hidden rounded-lg bg-black ring-1 ring-default">
      <video
        v-if="src && !failed"
        ref="video"
        :src="src"
        class="size-full"
        preload="metadata"
        playsinline
        @click="toggle"
        @play="onPlay"
        @pause="onPause"
        @seeked="tick"
        @loadedmetadata="onMetadata"
        @error="failed = true"
      />
      <div v-else class="flex size-full flex-col items-center justify-center gap-2 px-8 text-center">
        <UIcon name="i-lucide-video-off" class="size-8 text-ink-600" />
        <p class="text-sm text-muted">The video cannot be played.</p>
        <p v-if="devHint" class="text-xs text-dimmed">In <code>npm run dev</code>, set <code>TRUEMAIN_DEV_RECORDING</code> to a local MP4.</p>
      </div>
    </div>

    <div class="flex items-center gap-1">
      <UButton :icon="playing ? 'i-lucide-pause' : 'i-lucide-play'" color="neutral" variant="ghost" size="sm" :aria-label="playing ? 'Pause' : 'Play'" title="Play / pause (Space)" @click="toggle" />
      <UButton icon="i-lucide-skip-back" color="neutral" variant="ghost" size="sm" aria-label="Previous moment" title="Previous moment (Shift+←)" @click="previousMoment" />
      <UButton icon="i-lucide-skip-forward" color="neutral" variant="ghost" size="sm" aria-label="Next moment" title="Next moment (Shift+→)" @click="nextMoment" />
      <span class="ml-1 text-xs tabular-nums text-muted">
        <span class="text-highlighted">{{ formatClock(currentMs) }}</span> / {{ formatClock(length) }}
      </span>

      <div class="ml-auto flex items-center gap-1">
        <template v-if="editable">
          <UButton label="Set in" color="neutral" variant="outline" size="xs" title="Start a clip at the playhead (I)" @click="emit('setIn', currentMs)">
            <template #trailing><UKbd value="I" size="sm" /></template>
          </UButton>
          <UButton label="Set out" color="neutral" variant="outline" size="xs" title="End a clip at the playhead (O)" @click="emit('setOut', currentMs)">
            <template #trailing><UKbd value="O" size="sm" /></template>
          </UButton>
        </template>
        <UButton :icon="muted ? 'i-lucide-volume-x' : 'i-lucide-volume-2'" color="neutral" variant="ghost" size="sm" :aria-label="muted ? 'Unmute' : 'Mute'" title="Mute (M)" @click="toggleMute" />
        <UButton icon="i-lucide-maximize" color="neutral" variant="ghost" size="sm" aria-label="Full screen" @click="fullscreen" />
      </div>
    </div>

    <RecapTimeline
      :length-ms="length"
      :current-ms="currentMs"
      :moments="moments"
      :editable="editable"
      :drafts="drafts"
      :selected="selected"
      :saved="saved"
      :pending-in="pendingIn"
      @seek="seek"
      @moment="jumpTo"
      @create="(start, end) => emit('create', start, end)"
      @resize="(key, start, end) => emit('resize', key, start, end)"
      @select="key => emit('select', key)"
    />
  </div>
</template>
