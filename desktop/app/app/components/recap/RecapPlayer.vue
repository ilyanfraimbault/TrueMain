<script setup lang="ts">
import type { Moment } from '~/types/recordings'
import type { RecapWinProbability } from '~/utils/recording-win-probability'
import { formatClock } from '~/utils/recording-moments'

/**
 * A recording's video with the app's own controls and the timeline under it —
 * the recap of a full game (`editable`: ranges are cut on the timeline, I and O
 * set their ends at the playhead) or a saved clip's player. A jump to a moment
 * lands a few seconds before it, so the action is seen building up.
 *
 * Full screen turns the whole player immersive: the shell's window goes full
 * screen (an element's own full screen only fills the webview, not the screen)
 * and the controls and the timeline float over the video, shown while the
 * mouse moves or the video is paused.
 *
 * Keys: Space plays, ← → move 5 s, Shift+← → the previous / next moment, M mutes,
 * F toggles full screen, Esc leaves it, I / O set in / out.
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
  /** The game's win-probability curve on the video, drawn under the moments' lanes. */
  winProbability?: RecapWinProbability | null
}>(), { editable: false, drafts: () => [], selected: null, saved: () => [], pendingIn: null, winProbability: null })

const emit = defineEmits<{
  create: [startMs: number, endMs: number]
  resize: [key: string, startMs: number, endMs: number]
  select: [key: string | null]
  setIn: [ms: number]
  setOut: [ms: number]
}>()

/** How far before a moment a jump lands. */
const LEAD_MS = 5_000

/** How long the mouse rests before the full-screen controls fade out. */
const IDLE_MS = 2_500

const root = ref<HTMLElement | null>(null)
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

const jumpTo = (moment: Pick<Moment, 'videoMs'>) => seek(moment.videoMs - LEAD_MS)

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

const immersive = ref(false)
const idle = ref(false)
const hoveringControls = ref(false)
const controlsHidden = computed(() => immersive.value && idle.value && playing.value && !hoveringControls.value)
let idleTimer: ReturnType<typeof setTimeout> | undefined
let stopWatchingWindow: (() => void) | undefined

function wake() {
  idle.value = false
  clearTimeout(idleTimer)
  idleTimer = setTimeout(() => { idle.value = true }, IDLE_MS)
}

async function setWindowFullscreen(on: boolean) {
  if (insideTauri()) {
    const { getCurrentWindow } = await import('@tauri-apps/api/window')
    await getCurrentWindow().setFullscreen(on)
  }
  else if (on) await root.value?.requestFullscreen().catch(() => {})
  else if (document.fullscreenElement) await document.exitFullscreen()
}

async function enterFullscreen() {
  immersive.value = true
  wake()
  await setWindowFullscreen(true)
  if (insideTauri() && !stopWatchingWindow) {
    // Leaving through the OS (the green button, a Space swipe) drops the immersive view too.
    const { getCurrentWindow } = await import('@tauri-apps/api/window')
    const appWindow = getCurrentWindow()
    stopWatchingWindow = await appWindow.onResized(async () => {
      if (immersive.value && !(await appWindow.isFullscreen())) immersive.value = false
    })
  }
}

async function exitFullscreen() {
  immersive.value = false
  clearTimeout(idleTimer)
  await setWindowFullscreen(false)
}

const fullscreen = () => (immersive.value ? exitFullscreen() : enterFullscreen())

function onDocumentFullscreen() {
  if (!insideTauri() && !document.fullscreenElement) immersive.value = false
}
onMounted(() => document.addEventListener('fullscreenchange', onDocumentFullscreen))
onBeforeUnmount(() => {
  document.removeEventListener('fullscreenchange', onDocumentFullscreen)
  clearTimeout(idleTimer)
  stopWatchingWindow?.()
  if (immersive.value) void setWindowFullscreen(false)
})

defineShortcuts({
  ' ': toggle,
  arrowleft: () => seek(currentMs.value - 5_000),
  arrowright: () => seek(currentMs.value + 5_000),
  shift_arrowleft: previousMoment,
  shift_arrowright: nextMoment,
  m: toggleMute,
  f: fullscreen,
  escape: () => immersive.value && exitFullscreen(),
  i: () => props.editable && emit('setIn', currentMs.value),
  o: () => props.editable && emit('setOut', currentMs.value),
})

defineExpose({ seek, playRange, jumpTo, currentMs })
</script>

<template>
  <div class="min-w-0">
    <!-- Into <body> while immersive: an ancestor's backdrop filter would otherwise pin `fixed` to the page's card. -->
    <Teleport to="body" :disabled="!immersive">
      <div
        ref="root"
        :class="immersive ? ['fixed inset-0 z-50 bg-black', controlsHidden && 'cursor-none'] : 'flex min-w-0 flex-col gap-2'"
        @mousemove="immersive && wake()"
      >
        <div :class="immersive ? 'absolute inset-0' : 'relative aspect-video overflow-hidden rounded-lg bg-black ring-1 ring-default'">
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

        <div
          :class="immersive
            ? ['absolute inset-x-0 bottom-0 flex flex-col gap-2 bg-gradient-to-t from-black/90 via-black/70 to-transparent px-6 pt-16 pb-4 transition-opacity duration-300', controlsHidden && 'pointer-events-none opacity-0']
            : 'contents'"
          @mouseenter="hoveringControls = true"
          @mouseleave="hoveringControls = false"
        >
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
              <UButton :icon="immersive ? 'i-lucide-minimize' : 'i-lucide-maximize'" color="neutral" variant="ghost" size="sm" :aria-label="immersive ? 'Exit full screen' : 'Full screen'" :title="immersive ? 'Exit full screen (F / Esc)' : 'Full screen (F)'" @click="fullscreen" />
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
            :win-probability="winProbability"
            @seek="seek"
            @moment="jumpTo"
            @create="(start, end) => emit('create', start, end)"
            @resize="(key, start, end) => emit('resize', key, start, end)"
            @select="key => emit('select', key)"
          />
        </div>
      </div>
    </Teleport>
  </div>
</template>
