<script setup lang="ts">
import type { ComponentPublicInstance } from 'vue'
import { getPositionIconUrl } from '#shared/utils/ddragon'
import { championIconUrl, SAMPLE_ALLIES } from '~/utils/desktop-landing-sample'

/**
 * Champion select as the app plays it, redrawn for `/download` (#2072): the
 * ranked picks from the player's pool arrive with their one-line reason, then
 * the player hovers Ahri from the app and the lock button takes her. The picks'
 * order and reasons are a sample (the app leads each card with a measured
 * figure; a sample has none to show, so the drawing shows no figure at all).
 */
defineProps<{ patch: string | null }>()

const LANES = ['TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY'] as const

const PICKS = [
  { key: 'Ahri', reason: 'vs Orianna (likely)' },
  { key: 'Syndra', reason: 'Safe blind' },
  { key: 'Viktor', reason: 'With Vi' },
  { key: 'Galio', reason: 'Strong mid', adds: 'Magic damage' },
] as const

// Beats: picks arrive one by one, the pointer reaches Ahri's "Hover", clicks it,
// steps aside, then a hold.
const root = ref<HTMLElement | null>(null)
const hoverControl = ref<HTMLElement | null>(null)
// The "Hover" control sits in the picks' v-for, where a plain string ref would collect an array.
const setHoverControl = (el: Element | ComponentPublicInstance | null) => (hoverControl.value = el instanceof HTMLElement ? el : null)
const { beat, reached, playing } = useSceneBeats(root, [400, 260, 260, 260, 800, 500, 3200])
const picksIn = (index: number) => reached(1 + index)
const hovered = computed(() => reached(5))

/** Where the pointer rests: on the control it is about to click, measured in the scene's own box. */
const pointer = ref({ x: 0, y: 0, visible: false, pressed: false })
function centreOf(control: HTMLElement | null) {
  const scene = root.value
  if (!control || !scene) return { x: 0, y: 0 }
  const box = control.getBoundingClientRect()
  const origin = scene.getBoundingClientRect()
  return { x: box.left - origin.left + box.width / 2, y: box.top - origin.top + box.height / 2 }
}
watch(beat, (current) => {
  if (!playing.value) return
  const target = current >= 4 ? hoverControl.value : null
  const at = target ? centreOf(target) : { x: (root.value?.clientWidth ?? 0) * 0.6, y: (root.value?.clientHeight ?? 0) * 0.9 }
  pointer.value = { ...at, visible: current >= 4 && current < 6, pressed: current === 5 }
})

const canonicalIcon = useCanonicalIcon()
const loadingArt = useCanonicalLoadingArt()
const loading = (key: string) => loadingArt(`https://ddragon.leagueoflegends.com/cdn/img/champion/loading/${key}_0.jpg`)
</script>

<template>
  <div ref="root" class="relative overflow-hidden rounded-xl bg-ink-950 p-3 ring-1 ring-white/10 shadow-[0_30px_80px_-24px_rgb(0_0_0/0.9)] sm:p-4">
    <!-- The strip over the draft, as DraftTopStrip draws it: whose turn, the seconds left and their bar, the lock button. -->
    <div class="flex items-center gap-3 border-b border-default pb-3">
      <div class="flex min-w-0 flex-1 flex-col gap-1.5">
        <div class="flex items-baseline justify-between">
          <span class="stat-label">Your pick</span>
          <span class="stat-value text-xl leading-none">24</span>
        </div>
        <div class="h-1 overflow-hidden rounded-full bg-accented">
          <div class="h-full w-[80%] rounded-full bg-ink-300" />
        </div>
      </div>
      <span
        class="flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-xs font-medium transition-colors duration-300"
        :class="hovered ? 'bg-primary text-inverted' : 'bg-primary/10 text-primary/50'"
      ><UIcon name="i-lucide-lock" class="size-4" />Lock in{{ hovered ? ' Ahri' : '' }}</span>
    </div>

    <div class="mt-3 grid grid-cols-[auto_minmax(0,1fr)] gap-3 sm:gap-4">
      <!-- Our team, lane by lane; our own slot fills with the hover. -->
      <ul class="flex flex-col gap-1.5">
        <li v-for="(lane, index) in LANES" :key="lane" class="flex items-center gap-2 rounded-md p-1" :class="lane === 'MIDDLE' && 'bg-ally/10 ring-1 ring-ally/30'">
          <span class="relative size-8 overflow-hidden rounded bg-elevated">
            <img
              v-if="lane !== 'MIDDLE' && patch"
              :src="canonicalIcon(championIconUrl(SAMPLE_ALLIES[index]!, patch))"
              alt=""
              width="32"
              height="32"
              class="size-full"
            >
            <img
              v-else-if="patch"
              :src="canonicalIcon(championIconUrl('Ahri', patch))"
              alt=""
              width="32"
              height="32"
              class="size-full transition-opacity duration-300"
              :class="hovered ? 'opacity-60' : 'opacity-0'"
            >
          </span>
          <img :src="canonicalIcon(getPositionIconUrl(lane))" alt="" width="14" height="14" class="size-3.5 opacity-60">
        </li>
      </ul>

      <div class="min-w-0">
        <div class="flex items-center gap-3">
          <p class="text-sm font-semibold text-highlighted">Your pick</p>
          <span class="text-xs text-muted">Middle</span>
          <span class="ml-auto flex items-center gap-1.5 text-xs text-muted">
            <span class="flex h-4 w-7 items-center rounded-full bg-primary p-0.5"><span class="ml-auto size-3 rounded-full bg-white" /></span>
            My pool
          </span>
        </div>
        <ul class="mt-2 grid grid-cols-4 items-end gap-2">
          <li
            v-for="(pick, index) in PICKS"
            :key="pick.key"
            class="landing-rise relative overflow-hidden rounded-lg bg-elevated transition-shadow duration-300"
            :class="[{ 'is-on': picksIn(index) }, index === 0 && hovered ? 'ring-2 ring-primary shadow-[0_0_24px_-8px_var(--color-rosegold-400)]' : 'ring-1 ring-white/5']"
            :style="{ aspectRatio: `4 / ${(5 * Math.max(0.78, 1 - index * 0.035)).toFixed(3)}` }"
          >
            <div class="absolute inset-0 bg-cover bg-[50%_12%]" :style="{ backgroundImage: `url(${loading(pick.key)})` }" />
            <div class="absolute inset-0 bg-gradient-to-t from-ink-950 via-ink-950/40 via-30% to-transparent to-60%" />
            <span
              v-if="index === 0"
              :ref="setHoverControl"
              class="absolute right-1.5 top-1.5 flex items-center gap-1 rounded-md bg-primary/10 px-1.5 py-1 text-[10px] font-medium leading-none text-primary ring-1 ring-inset ring-primary/25 backdrop-blur-sm"
            ><UIcon name="i-lucide-hand" class="size-3" />{{ hovered ? 'Hovered' : 'Hover' }}</span>
            <div class="absolute inset-x-0 bottom-0 flex flex-col gap-0.5 p-1.5 sm:p-2">
              <p class="truncate text-[11px] font-semibold text-highlighted sm:text-[13px]">{{ pick.key }}</p>
              <p class="truncate text-[9px] leading-tight text-muted sm:text-[11px]">{{ pick.reason }}</p>
              <span v-if="'adds' in pick" class="mt-0.5 hidden w-fit items-center gap-1 whitespace-nowrap rounded bg-ink-950/70 px-1.5 py-0.5 text-[10px] font-medium leading-none text-stat-mr ring-1 ring-white/10 sm:flex">
                <UIcon name="i-lucide-plus" class="size-2.5" />{{ pick.adds }}
              </span>
            </div>
          </li>
        </ul>
      </div>
    </div>

    <!-- The pointer, only while the scene plays. -->
    <UIcon
      v-if="playing"
      name="i-lucide-mouse-pointer-2"
      class="landing-pointer absolute left-0 top-0 size-5 text-white drop-shadow-[0_1px_2px_rgb(0_0_0/0.8)]"
      :class="[!pointer.visible && 'opacity-0', pointer.pressed && 'scale-85']"
      :style="{ translate: `${pointer.x - 4}px ${pointer.y - 3}px` }"
    />
  </div>
</template>

<style scoped>
.landing-rise {
  opacity: 0;
  translate: 0 10px;
  transition: opacity 400ms cubic-bezier(0.2, 0, 0, 1), translate 400ms cubic-bezier(0.2, 0, 0, 1), box-shadow 300ms ease;
}

.landing-rise.is-on {
  opacity: 1;
  translate: 0 0;
}

.landing-pointer {
  transition: translate 750ms cubic-bezier(0.4, 0, 0.2, 1), opacity 300ms ease, scale 120ms ease;
}

@media (prefers-reduced-motion: reduce) {
  .landing-rise { transition: none; }
}
</style>
