<script setup lang="ts">
import { championIconUrl } from '~/utils/desktop-landing-sample'

/**
 * A recording's recap, redrawn for `/download` (#2072) as the app lays it out
 * (`desktop/app/app/pages/recordings/[id].vue`): the player over its control
 * row, the timeline — every event a dot above the track, blue our side's way,
 * red the enemy's, the win-probability curve under them, the clock — and the
 * side panel's Clips tab. The video is a real frame of one of our own games,
 * paused at 13:23 (`public/desktop/game.webp`); the moments around it are a
 * sample, its champions the frame's lobby (`desktop-landing-sample.ts`).
 * The scene: the playhead goes to the triple kill and its dot opens the
 * tooltip the app shows on hover (#2171) — who killed whom, the drake taken
 * beside it — past a moment marked with the mark key (#2079), then a range is dragged across it, the app names it, it is
 * saved and joins the saved clips.
 */
defineProps<{ patch: string | null }>()

const root = ref<HTMLElement | null>(null)
const { reached, playing } = useSceneBeats(root, [500, 1400, 900, 1400, 3600])
const canonicalIcon = useCanonicalIcon()

/** The recording's length, in seconds of video. */
const LENGTH = 1900

/**
 * The events' dots, as the app merges them at this width: where each sits, in
 * seconds of video, and which side it went — `mixed` for a dot holding both.
 */
const DOTS = [
  { at: 228, side: 'ally' },
  // A death, then voidgrubs and a kill: both ways, one dot.
  { at: 371, side: 'mixed', many: true },
  { at: 570, side: 'ally' },
  { at: 627, side: 'ally' },
  { at: 760, side: 'ally' },
  { at: 807, side: 'ally', many: true },
  { at: 950, side: 'enemy' },
  { at: 1045, side: 'enemy' },
  { at: 1102, side: 'ally' },
  { at: 1216, side: 'ally' },
  { at: 1292, side: 'ally' },
  { at: 1349, side: 'ally' },
  // A press of the mark key: its own dot, and a line across the panel.
  { at: 1460, side: 'mark' },
  { at: 1577, side: 'ally' },
  { at: 1634, side: 'ally' },
] as const
const MOMENTS = 17
const MARKS = DOTS.filter(dot => dot.side === 'mark')

/** The triple kill's dot: its tooltip, as the app lists a dot's events. */
const TOOLTIP_AT = 807
const MINIMAP = 'https://raw.communitydragon.org/latest/game/assets/ux/minimap/icons'
const TRIPLE_KILL = { killer: 'Yone', victims: ['Orianna', 'Ezreal', 'Nautilus'], at: 795 }
const DRAKE = { icon: `${MINIMAP}/dragon_infernal.png`, label: 'Infernal Drake', at: 820 }

const RANGE = { from: 768, to: 813 }
const SAVED = [
  { title: 'Kill + Dragon', from: 552, to: 583, length: '0:31' },
  { title: 'Double kill', from: 1336, to: 1355, length: '0:19' },
]
const NEW_CLIP = { title: 'Triple kill on Yone', length: '0:45' }

/** The win-probability curve from our side, 0..1 at each minute of the 31-minute recording. */
const CURVE = [0.5, 0.5, 0.52, 0.49, 0.46, 0.44, 0.47, 0.53, 0.58, 0.57, 0.55, 0.51, 0.48, 0.56, 0.61, 0.6, 0.63, 0.58, 0.54, 0.57, 0.64, 0.7, 0.68, 0.72, 0.79, 0.76, 0.82, 0.88, 0.93, 0.97, 1, 1]
const SWINGS = [{ minute: 8, up: true }, { minute: 12, up: false }, { minute: 13, up: true }, { minute: 21, up: true }]
const curvePoint = (minute: number) => ({ x: (minute * 60 / LENGTH) * 1000, y: (1 - CURVE[minute]!) * 100 })
const curveLine = CURVE.map((_, minute) => curvePoint(minute)).map(({ x, y }) => `${x.toFixed(1)},${y.toFixed(1)}`).join(' ')
const curveArea = `0,50 ${curveLine} ${curvePoint(CURVE.length - 1).x.toFixed(1)},50`
const curveClip = `landing-recap-${useId()}`

/** The app's timeline geometry (`utils/recording-timeline.ts`), in pixels: the dots' row, then the curve's panel. */
const PANEL_TOP = 20
const PANEL = 64
const TICKS = [0, 300, 600, 900, 1200, 1500, 1800]

const SIDE_DOT = {
  ally: 'bg-ally',
  enemy: 'bg-enemy',
  mixed: 'bg-[linear-gradient(90deg,var(--color-ally)_50%,var(--color-enemy)_50%)]',
  mark: 'bg-primary',
} as const

const playhead = computed(() => (reached(1) ? 803 : 76))
const cut = computed(() => reached(2))
const drafted = computed(() => reached(3) && !reached(4))
const saved = computed(() => reached(4))
const clips = computed(() => (saved.value ? [NEW_CLIP, ...SAVED] : SAVED))
/** The tooltip opens as the playhead lands on the triple kill, and stays: it is what the scene shows. */
const hovering = computed(() => reached(1))

const pct = (seconds: number) => `${((seconds / LENGTH) * 100).toFixed(2)}%`
const clock = (seconds: number) => `${Math.floor(seconds / 60)}:${String(Math.round(seconds % 60)).padStart(2, '0')}`
</script>

<template>
  <div ref="root" class="grid gap-4 lg:grid-cols-[minmax(0,1fr)_16rem]">
    <div class="flex min-w-0 flex-col gap-2">
      <div class="relative aspect-video overflow-hidden rounded-lg bg-black ring-1 ring-default shadow-[0_30px_80px_-24px_rgb(0_0_0/0.9)]">
        <SkeletonPicture src="/desktop/game.webp" :width="1280" :height="720" alt="" loading="lazy" class="absolute inset-0" />
      </div>

      <!-- The control row under the video, as RecapPlayerControls draws it. -->
      <div class="flex items-center gap-1 text-default">
        <span class="p-1.5"><UIcon name="i-lucide-play" class="block size-5" /></span>
        <span class="p-1.5"><UIcon name="i-lucide-skip-back" class="block size-5" /></span>
        <span class="p-1.5"><UIcon name="i-lucide-skip-forward" class="block size-5" /></span>
        <span class="ml-1 text-xs tabular-nums text-muted">
          <span class="text-highlighted">{{ clock(reached(2) ? 803 : 76) }}</span> / 31:40
        </span>
        <div class="ml-auto hidden items-center gap-1 sm:flex">
          <span v-for="key in ['in', 'out']" :key="key" class="flex items-center gap-1 rounded-md px-2 py-1 text-xs font-medium ring-1 ring-inset ring-accented">
            Set {{ key }} <kbd class="rounded bg-elevated px-1 font-sans text-[10px] text-highlighted ring-1 ring-inset ring-accented">{{ key === 'in' ? 'I' : 'O' }}</kbd>
          </span>
          <span class="px-2.5 py-1.5 text-sm font-medium tabular-nums">1×</span>
          <span class="p-1.5"><UIcon name="i-lucide-volume-2" class="block size-5" /></span>
          <span class="p-1.5"><UIcon name="i-lucide-maximize" class="block size-5" /></span>
        </div>
      </div>

      <!-- The timeline: the curve's glyph beside its lane, then the track. -->
      <div class="flex gap-2">
        <div class="relative w-4 shrink-0" :style="{ height: `${PANEL_TOP + PANEL}px` }">
          <UIcon name="i-lucide-chart-spline" class="absolute left-0.5 size-3.5 -translate-y-1/2 text-ally" :style="{ top: `${PANEL_TOP + PANEL / 2}px` }" />
        </div>

        <div class="relative min-w-0 flex-1" :style="{ height: `${PANEL_TOP + PANEL}px` }">
          <span
            v-for="dot in DOTS"
            :key="dot.at"
            class="absolute top-2.5 -translate-1/2 rounded-full ring-2 ring-(--ui-bg) transition-transform duration-300"
            :class="[SIDE_DOT[dot.side], 'many' in dot ? 'size-3' : 'size-2.5', hovering && dot.at === TOOLTIP_AT && 'z-10 scale-150']"
            :style="{ left: pct(dot.at) }"
          />

          <!-- The dot's tooltip, as the app opens it under the pointer. -->
          <div
            class="pointer-events-none absolute bottom-full z-30 mb-1.5 -translate-x-1/2 rounded-md bg-ink-950/95 px-2 py-1.5 text-xs shadow-lg ring-1 ring-default backdrop-blur transition-opacity duration-300"
            :class="hovering ? 'opacity-100' : 'opacity-0'"
            :style="{ left: pct(TOOLTIP_AT) }"
          >
            <div class="grid grid-cols-[auto_1fr_auto] items-center gap-x-2 gap-y-1.5 whitespace-nowrap">
              <span class="flex items-center gap-1">
                <img v-if="patch" :src="canonicalIcon(championIconUrl(TRIPLE_KILL.killer, patch))" alt="" width="20" height="20" class="size-5 rounded ring-1 ring-ally">
                <UIcon name="i-lucide-swords" class="size-3.5 text-ally" />
                <template v-if="patch">
                  <img v-for="victim in TRIPLE_KILL.victims" :key="victim" :src="canonicalIcon(championIconUrl(victim, patch))" alt="" width="20" height="20" class="size-5 rounded ring-1 ring-enemy">
                </template>
              </span>
              <span class="text-highlighted">Triple kill</span>
              <span class="pl-3 text-right tabular-nums text-dimmed">{{ clock(TRIPLE_KILL.at) }}</span>
              <img :src="canonicalIcon(DRAKE.icon)" alt="" width="20" height="20" class="size-5 object-contain">
              <span class="text-highlighted">{{ DRAKE.label }}</span>
              <span class="pl-3 text-right tabular-nums text-dimmed">{{ clock(DRAKE.at) }}</span>
            </div>
          </div>

          <div class="absolute inset-x-0 overflow-hidden rounded-md bg-muted ring-1 ring-default" :style="{ top: `${PANEL_TOP}px`, height: `${PANEL}px` }">
            <div class="absolute inset-y-0 left-0 bg-white/[0.035] transition-[width] duration-[1400ms] ease-in-out" :style="{ width: pct(playhead) }" />
            <div v-for="at in TICKS" :key="`grid-${at}`" class="absolute inset-y-0 w-px bg-white/[0.04]" :style="{ left: pct(at) }" />
            <div v-for="mark in MARKS" :key="`mark-${mark.at}`" class="absolute inset-y-0 w-px bg-primary/60" :style="{ left: pct(mark.at) }" />
            <div
              v-for="clip in SAVED"
              :key="clip.title"
              class="absolute bottom-0 h-1 rounded-full bg-gold/70"
              :style="{ left: pct(clip.from), width: pct(clip.to - clip.from) }"
            />
            <div
              class="absolute bottom-0 h-1 rounded-full bg-gold/70 transition-opacity duration-300"
              :class="saved ? 'opacity-100' : 'opacity-0'"
              :style="{ left: pct(RANGE.from), width: pct(RANGE.to - RANGE.from) }"
            />
            <svg class="absolute inset-0 size-full overflow-visible" viewBox="0 0 1000 100" preserveAspectRatio="none" aria-hidden="true">
              <defs>
                <clipPath :id="`${curveClip}-ally`"><rect x="0" y="0" width="1000" height="50" /></clipPath>
                <clipPath :id="`${curveClip}-enemy`"><rect x="0" y="50" width="1000" height="50" /></clipPath>
              </defs>
              <line x1="0" x2="1000" y1="50" y2="50" class="stroke-white/15" stroke-dasharray="3 3" vector-effect="non-scaling-stroke" />
              <polygon :points="curveArea" class="fill-ally/20" :clip-path="`url(#${curveClip}-ally)`" />
              <polygon :points="curveArea" class="fill-enemy/20" :clip-path="`url(#${curveClip}-enemy)`" />
              <polyline :points="curveLine" class="fill-none stroke-ink-200" stroke-width="1.5" stroke-linejoin="round" vector-effect="non-scaling-stroke" />
            </svg>
          </div>

          <span
            v-for="swing in SWINGS"
            :key="swing.minute"
            class="absolute size-2.5 -translate-1/2 rounded-full ring-2 ring-(--ui-bg-muted)"
            :class="swing.up ? 'bg-ally' : 'bg-enemy'"
            :style="{ left: pct(swing.minute * 60), top: `${PANEL_TOP + (curvePoint(swing.minute).y / 100) * PANEL}px` }"
          />

          <!-- The range being cut: a band across the panel, a handle at each end. -->
          <div
            class="absolute rounded-md bg-primary/20 ring-1 ring-primary transition-[width,opacity] duration-700 ease-out"
            :class="cut && !saved ? 'opacity-100' : 'opacity-0'"
            :style="{ top: `${PANEL_TOP}px`, height: `${PANEL}px`, left: pct(RANGE.from), width: cut ? pct(RANGE.to - RANGE.from) : '0%' }"
          >
            <span class="absolute -left-1 inset-y-0 w-2 rounded-sm bg-primary" />
            <span class="absolute -right-1 inset-y-0 w-2 rounded-sm bg-primary" />
          </div>

          <div
            class="absolute bottom-0 z-20 w-0.5 -translate-x-1/2 bg-inverted transition-[left] duration-[1400ms] ease-in-out"
            :class="!playing && 'hidden'"
            :style="{ top: `${PANEL_TOP - 6}px`, left: pct(playhead) }"
          >
            <span class="absolute -left-[5px] -top-1.5 size-3 rounded-full bg-inverted ring-2 ring-(--ui-bg)" />
          </div>
        </div>
      </div>
      <div class="relative ml-6 h-3 text-[10px] tabular-nums text-dimmed">
        <span v-for="at in TICKS" :key="at" class="absolute -translate-x-1/2" :style="{ left: pct(at) }">{{ clock(at) }}</span>
      </div>
    </div>

    <!-- The side panel's Clips tab: the hint, then the range being cut, then the saved clips. -->
    <aside class="flex flex-col gap-2">
      <div class="flex gap-4 border-b border-default text-xs font-medium">
        <span class="-mb-px border-b-2 border-primary pb-2 text-primary">Clips <span class="ml-0.5 rounded bg-primary/10 px-1 text-[10px]">{{ SAVED.length + (drafted || saved ? 1 : 0) }}</span></span>
        <span class="pb-2 text-muted">Moments <span class="ml-0.5 rounded bg-elevated px-1 text-[10px]">{{ MOMENTS }}</span></span>
        <span class="pb-2 text-muted">Swings</span>
      </div>

      <p v-if="!drafted" class="rounded-lg bg-elevated p-3 text-xs leading-relaxed text-muted">
        Drag across the timeline to cut a clip, or press
        <kbd class="rounded bg-accented px-1 font-sans text-[10px] text-highlighted">I</kbd> and
        <kbd class="rounded bg-accented px-1 font-sans text-[10px] text-highlighted">O</kbd> at the playhead.
      </p>
      <div v-else class="landing-draft flex flex-col gap-1.5 rounded-lg bg-accented p-2 ring-1 ring-primary/60">
        <p class="px-1 text-sm font-medium text-highlighted">{{ NEW_CLIP.title }}</p>
        <div class="flex items-center gap-1">
          <span class="text-[11px] tabular-nums text-muted">{{ clock(RANGE.from) }} → {{ clock(RANGE.to) }} <span class="text-dimmed">· {{ NEW_CLIP.length }}</span></span>
          <span class="ml-auto flex items-center gap-0.5">
            <span class="p-1 text-muted"><UIcon name="i-lucide-play" class="block size-4" /></span>
            <span class="p-1 text-muted"><UIcon name="i-lucide-x" class="block size-4" /></span>
            <span class="flex items-center gap-1 rounded-md bg-primary px-2 py-1 text-xs font-medium text-inverted"><UIcon name="i-lucide-save" class="size-4" />Save</span>
          </span>
        </div>
      </div>

      <h3 class="stat-label mt-2 px-1">Saved clips · {{ SAVED.length + (saved ? 1 : 0) }}</h3>
      <ul>
        <li
          v-for="clip in clips"
          :key="clip.title"
          class="flex items-center gap-2 rounded-md px-2 py-1.5 text-xs"
          :class="clip === NEW_CLIP && 'landing-draft'"
        >
          <UIcon name="i-lucide-scissors" class="size-3.5 shrink-0 text-primary" />
          <span class="truncate text-default">{{ clip.title }}</span>
          <span class="ml-auto shrink-0 tabular-nums text-dimmed">{{ clip.length }}</span>
        </li>
      </ul>
    </aside>
  </div>
</template>

<style scoped>
.landing-draft {
  animation: landing-draft-in 350ms cubic-bezier(0.2, 0, 0, 1);
}

@keyframes landing-draft-in {
  from { opacity: 0; translate: 0 6px; }
}

@media (prefers-reduced-motion: reduce) {
  .landing-draft { animation: none; }
}
</style>
