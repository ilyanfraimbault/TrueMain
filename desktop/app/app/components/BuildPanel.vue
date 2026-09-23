<script setup lang="ts">
import type { BuildSubject } from '~/composables/useDraftBuild'
import { laneIconUrl } from '~/types/draft'
import { formatGoldDiff, laneVerdict, winRateTone } from '~/utils/lane-verdict'

/**
 * What the draft is for: the build to run, against this draft — ours, or the
 * build of whichever champion was clicked — recomputed on every lock (see
 * `useDraftBuild`). Its header says how the lane goes.
 */
const props = defineProps<{
  subject: BuildSubject | null
  /** The build on screen is another champion's, not ours. */
  viewingOther: boolean
  secondsLeft: number
}>()

defineEmits<{ back: [] }>()

const { build, pending, error } = useDraftBuild(toRef(props, 'subject'))

const winRate = computed(() => (build.value && build.value.games > 0 ? build.value.wins / build.value.games : null))

const lane = computed(() => build.value?.lane ?? null)
const noun = computed(() => (props.subject?.request.position === 'JUNGLE' ? 'matchup' : 'lane'))
/** How hard the lane is: the site's own verdict, from the gold gap at 15 over the same games. */
const verdict = computed(() => laneVerdict(lane.value?.averageGoldDiffAt15 ?? null, lane.value?.measuredGames ?? 0, noun.value))

/**
 * The build fills the body in both directions without stretching its wells.
 * It picks the arrangement that matches the body's shape — path beside the
 * runes when the body is wide, under them when it is tall — lays it out at
 * that arrangement's natural width, and zooms it until it meets the body's
 * width or height. The other direction then takes the slack as extra width,
 * so the block spans the body instead of floating in it. Never below its own
 * size: past that it scrolls.
 */
const MAX_ZOOM = 1.6
/** Natural widths, in CSS pixels, of the two arrangements. */
const BASE_WIDTH = { stacked: 576, wide: 760 }
/** Wider than this (width over height) and the path goes beside the runes. */
const WIDE_RATIO = 1.2

const body = ref<HTMLElement | null>(null)
const content = ref<{ $el: HTMLElement } | null>(null)
const wide = ref(false)
const zoom = ref(1)
const width = ref(BASE_WIDTH.stacked)

async function fit() {
  const box = body.value
  if (!box) return
  const style = getComputedStyle(box)
  const room = {
    width: box.clientWidth - Number.parseFloat(style.paddingLeft) - Number.parseFloat(style.paddingRight),
    height: box.clientHeight - Number.parseFloat(style.paddingTop) - Number.parseFloat(style.paddingBottom),
  }
  wide.value = room.width / room.height > WIDE_RATIO
  const base = wide.value ? BASE_WIDTH.wide : BASE_WIDTH.stacked
  width.value = Math.min(base, room.width)
  await nextTick()

  const block = content.value?.$el
  if (!block) return
  // `scrollHeight` is in the block's own, unzoomed units.
  const next = Math.min(room.width / width.value, room.height / block.scrollHeight, MAX_ZOOM)
  zoom.value = Math.max(1, Math.floor(next * 100) / 100)
  width.value = Math.max(width.value, Math.floor(room.width / zoom.value))
}

let observer: ResizeObserver | null = null
watch(body, (box) => {
  observer?.disconnect()
  if (!box) return
  observer = new ResizeObserver(() => void fit())
  observer.observe(box)
})
// A new build brings a new tree, and with it a new height.
watch(build, () => nextTick(fit))
onBeforeUnmount(() => observer?.disconnect())

/** The last ten seconds are when a pick is decided; the timer says so. */
const urgent = computed(() => props.secondsLeft > 0 && props.secondsLeft <= 10)
</script>

<template>
  <section class="glass relative flex min-h-0 flex-col overflow-hidden rounded-2xl">
    <!-- The thin bar that says a newer draft is being asked for, without hiding the answer on screen. -->
    <div v-if="pending" class="absolute inset-x-0 top-0 z-10 h-0.5 overflow-hidden">
      <div class="h-full w-1/3 animate-[tm-scan_1.1s_ease-in-out_infinite] bg-gradient-to-r from-transparent via-primary to-transparent" />
    </div>

    <header class="flex items-center gap-3 border-b border-white/5 px-4 py-3">
      <UButton
        v-if="viewingOther"
        size="xs"
        variant="ghost"
        color="neutral"
        icon="i-lucide-arrow-left"
        aria-label="Back to your build"
        @click="$emit('back')"
      />
      <div v-if="subject" class="relative shrink-0">
        <ChampionPortrait :champion-id="subject.championId" size="md" class="!size-10 !rounded-lg" />
        <img :src="laneIconUrl(subject.request.position)" alt="" class="absolute -bottom-1.5 -right-1.5 size-5 rounded-full bg-ink-950 p-0.5 ring-1 ring-white/15">
      </div>

      <template v-if="build">
        <div class="flex flex-col leading-tight">
          <span class="text-xl font-bold tabular-nums tracking-tight" :class="winRateTone(winRate)">
            {{ winRate === null ? '—' : `${(winRate * 100).toFixed(1)}%` }}
          </span>
          <span class="text-[11px] tabular-nums text-dimmed">{{ build.games.toLocaleString('en-US') }} games</span>
        </div>

        <div v-if="lane" class="flex items-center gap-2 border-l border-white/10 pl-3">
          <UBadge v-if="verdict" :color="verdict.color" :variant="verdict.variant" size="md">{{ verdict.label }}</UBadge>
          <div class="flex flex-col leading-tight">
            <span class="text-sm font-semibold tabular-nums" :class="winRateTone(lane.winRate)">
              {{ lane.winRate === null ? '—' : `${Math.round(lane.winRate * 100)}%` }}
            </span>
            <span v-if="lane.averageGoldDiffAt15 !== null" class="text-[11px] tabular-nums text-dimmed">
              {{ formatGoldDiff(lane.averageGoldDiffAt15) }} gold @15
            </span>
          </div>
        </div>

        <UBadge v-if="build.source === 'standard'" color="warning" variant="subtle" size="sm">Standard build</UBadge>
      </template>

      <span
        v-if="secondsLeft > 0"
        class="ml-auto text-3xl font-bold tabular-nums leading-none tracking-tight transition-colors"
        :class="urgent ? 'text-primary drop-shadow-[0_0_12px_var(--color-rosegold-500)]' : 'text-highlighted'"
      >{{ secondsLeft }}</span>
    </header>

    <div v-if="!subject" class="m-auto p-8">
      <UIcon name="i-lucide-scroll-text" class="size-8 text-dimmed" />
    </div>

    <div v-else-if="!build && pending" class="m-auto p-8">
      <UIcon name="i-lucide-loader-circle" class="size-6 animate-spin text-dimmed" />
    </div>

    <div v-else-if="!build" class="m-auto flex flex-col items-center gap-2 p-8 text-center">
      <UIcon name="i-lucide-wifi-off" class="size-6 text-dimmed" />
      <p class="text-sm text-muted">No build for this pick yet</p>
      <p v-if="error" class="max-w-xs text-xs text-dimmed">{{ error }}</p>
    </div>

    <div v-else ref="body" class="flex min-h-0 flex-1 overflow-y-auto p-4">
      <BuildCore
        ref="content"
        class="m-auto shrink-0"
        :wide="wide"
        :style="{ zoom, width: `${width}px` }"
        :core="build.core"
        :champion-id="build.championId"
        :first-item-id="build.firstItemId"
        :build-tree="build.buildTree"
      />
    </div>
  </section>
</template>
