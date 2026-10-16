<script setup lang="ts">
import type { ComponentPublicInstance } from 'vue'
import type { RuneTreeResponse } from '#shared/types/static-data'
import { getPositionIconUrl } from '#shared/utils/ddragon'
import { championIconUrl, SAMPLE_ALLIES } from '~/utils/desktop-landing-sample'

/**
 * Champion select as the app plays it, redrawn for `/download` (#2072): the
 * ranked picks from the player's pool arrive with their one-line reason, the
 * player hovers Ahri from the app, then imports the build's rune page with
 * the small icon in the runes' corner — the hover-to-import beat. The picks'
 * order and reasons are a sample (the app leads each card with a measured
 * figure; a sample has none to show, so the drawing shows no figure at all).
 */
const props = defineProps<{ patch: string | null, runeTree: RuneTreeResponse | null }>()

const LANES = ['TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY'] as const

const PICKS = [
  { key: 'Ahri', reason: 'vs Orianna (likely)' },
  { key: 'Syndra', reason: 'Safe blind' },
  { key: 'Viktor', reason: 'With Vi' },
  { key: 'Galio', reason: 'Strong mid', adds: 'Magic damage' },
] as const

// Beats: picks arrive one by one, the pointer reaches Ahri's "Hover" and clicks
// it, the build opens, the pointer travels to the import icon, its tooltip, the
// click and its toast, then a hold.
const root = ref<HTMLElement | null>(null)
const hoverControl = ref<HTMLElement | null>(null)
// The "Hover" control sits in the picks' v-for, where a plain string ref would collect an array.
const setHoverControl = (el: Element | ComponentPublicInstance | null) => (hoverControl.value = el instanceof HTMLElement ? el : null)
const importControl = ref<HTMLElement | null>(null)
const { beat, reached, playing } = useSceneBeats(root, [700, 260, 260, 260, 800, 900, 900, 1100, 600, 3200])
const picksIn = (index: number) => reached(1 + index)
const hovered = computed(() => reached(5))
const pointerAtImport = computed(() => playing.value && reached(6) && !reached(9))
const tooltip = computed(() => playing.value && reached(7) && !reached(8))
const imported = computed(() => reached(8))

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
  const target = current >= 6 ? importControl.value : current >= 4 ? hoverControl.value : null
  const at = target ? centreOf(target) : { x: (root.value?.clientWidth ?? 0) * 0.6, y: (root.value?.clientHeight ?? 0) * 0.9 }
  pointer.value = { ...at, visible: current >= 4 && current < 9, pressed: current === 5 || current === 8 }
})

const canonicalIcon = useCanonicalIcon()
const loadingArt = useCanonicalLoadingArt()
const loading = (key: string) => loadingArt(`https://ddragon.leagueoflegends.com/cdn/img/champion/loading/${key}_0.jpg`)

/** Ahri's page as the drawing shows it: Domination's first keystone and minors, two Sorcery minors. */
const runes = computed(() => {
  const tree = props.runeTree
  const domination = tree?.styles.find(style => style.styleId === 8100)
  const sorcery = tree?.styles.find(style => style.styleId === 8200)
  if (!tree || !domination || !sorcery) return null
  const perk = (id: number | undefined) => (id === undefined ? null : tree.perks[id] ?? null)
  return {
    keystone: perk(domination.keystones[0]),
    primary: domination.subRows.map(row => perk(row[0])),
    secondary: [perk(sorcery.subRows[0]?.[0]), perk(sorcery.subRows[2]?.[1])],
    secondaryStyle: tree.perkStyles[8200] ?? null,
  }
})
</script>

<template>
  <div ref="root" class="relative overflow-hidden rounded-xl bg-ink-950 p-3 ring-1 ring-white/10 shadow-[0_30px_80px_-24px_rgb(0_0_0/0.9)] sm:p-4">
    <!-- The strip over the draft: whose turn, the clock, the lock button. -->
    <div class="flex items-center justify-between gap-3 border-b border-default pb-3">
      <p class="text-sm font-semibold text-highlighted">Your pick</p>
      <div class="flex items-center gap-3">
        <span class="stat-value text-sm text-muted">0:24</span>
        <span
          class="rounded-md px-2.5 py-1 text-xs font-semibold transition-colors duration-300"
          :class="hovered ? 'keycap bg-primary text-inverted' : 'bg-elevated text-dimmed'"
        >Lock in{{ hovered ? ' Ahri' : '' }}</span>
      </div>
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
        <p class="text-[11px] font-medium text-dimmed">My pool · Middle</p>
        <ul class="mt-2 grid grid-cols-4 gap-2">
          <li
            v-for="(pick, index) in PICKS"
            :key="pick.key"
            class="landing-rise relative aspect-[4/5] overflow-hidden rounded-lg bg-elevated transition-shadow duration-300"
            :class="[{ 'is-on': picksIn(index) }, index === 0 && hovered ? 'ring-2 ring-primary shadow-[0_0_24px_-8px_var(--color-rosegold-400)]' : 'ring-1 ring-white/5']"
          >
            <div class="absolute inset-0 bg-cover bg-[50%_12%]" :style="{ backgroundImage: `url(${loading(pick.key)})` }" />
            <div class="absolute inset-0 bg-gradient-to-t from-ink-950 via-ink-950/40 via-30% to-transparent to-60%" />
            <span
              v-if="index === 0"
              :ref="setHoverControl"
              class="absolute right-1 top-1 rounded bg-ink-950/80 px-1.5 py-0.5 text-[9px] font-semibold ring-1 ring-white/10 transition-colors duration-300"
              :class="hovered ? 'text-primary' : 'text-muted'"
            >{{ hovered ? 'Hovered' : 'Hover' }}</span>
            <div class="absolute inset-x-0 bottom-0 flex flex-col gap-0.5 p-1.5 sm:p-2">
              <p class="truncate text-[11px] font-semibold text-highlighted sm:text-[13px]">{{ pick.key }}</p>
              <p class="truncate text-[9px] leading-tight text-muted sm:text-[11px]">{{ pick.reason }}</p>
              <span v-if="'adds' in pick" class="mt-0.5 hidden w-fit items-center gap-0.5 rounded bg-ink-950/70 px-1 py-0.5 text-[9px] font-medium leading-none text-stat-mr ring-1 ring-white/10 sm:flex">
                <UIcon name="i-lucide-plus" class="size-2" />{{ pick.adds }}
              </span>
            </div>
          </li>
        </ul>

        <!-- The build for the hovered pick: its rune page, the import icon in its corner. -->
        <div class="landing-rise relative mt-3 rounded-lg bg-elevated/60 p-2.5 ring-1 ring-white/5" :class="{ 'is-on': hovered }">
          <p class="text-[11px] font-medium text-dimmed">Ahri · runes against this draft</p>
          <div class="mt-2 flex items-center gap-1.5">
            <template v-if="runes">
              <img v-if="runes.keystone" :src="canonicalIcon(runes.keystone.iconUrl)" alt="" width="32" height="32" class="size-8">
              <img v-for="(perk, index) in runes.primary" :key="`p${index}`" :src="canonicalIcon(perk?.iconUrl)" alt="" width="20" height="20" class="size-5 rounded-full bg-ink-950">
              <span class="mx-1 h-5 w-px bg-white/10" />
              <img v-if="runes.secondaryStyle" :src="canonicalIcon(runes.secondaryStyle.iconUrl)" alt="" width="18" height="18" class="size-4.5 opacity-80">
              <img v-for="(perk, index) in runes.secondary" :key="`s${index}`" :src="canonicalIcon(perk?.iconUrl)" alt="" width="20" height="20" class="size-5 rounded-full bg-ink-950">
            </template>
            <span v-for="n in (runes ? 0 : 7)" v-else :key="n" class="size-5 rounded-full bg-ink-950" />
          </div>
          <span
            ref="importControl"
            class="absolute right-2 top-2 flex size-6 items-center justify-center rounded-md transition-colors duration-200"
            :class="pointerAtImport ? 'bg-accented text-highlighted' : 'text-dimmed'"
          >
            <UIcon :name="imported ? 'i-lucide-check' : 'i-lucide-download'" class="size-3.5" />
          </span>
          <span
            class="pointer-events-none absolute -top-7 right-0 whitespace-nowrap rounded-md bg-inverted px-2 py-1 text-[11px] font-medium text-inverted transition-opacity duration-200"
            :class="tooltip ? 'opacity-100' : 'opacity-0'"
          >Import runes into the client</span>
        </div>
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

    <div
      class="absolute bottom-3 left-1/2 flex w-max max-w-[90%] -translate-x-1/2 items-center gap-2 rounded-lg bg-elevated px-3 py-2 text-xs text-highlighted ring-1 ring-white/10 shadow-lg transition-all duration-300"
      :class="imported ? 'translate-y-0 opacity-100' : 'translate-y-3 opacity-0'"
    >
      <UIcon name="i-lucide-circle-check" class="size-4 shrink-0 text-primary" />
      <span><span class="font-semibold">Runes imported</span> · The TrueMain page is selected in the client.</span>
    </div>
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
