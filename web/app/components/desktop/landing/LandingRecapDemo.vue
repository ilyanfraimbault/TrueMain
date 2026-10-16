<script setup lang="ts">
import { championSplashUrl, SAMPLE_ME } from '~/utils/desktop-landing-sample'

/**
 * The recap, redrawn for `/download` (#2072): the recorded game over its
 * timeline — kills, assists and deaths each on their lane, objectives above,
 * the playhead — then a clip cut by hand across a triple kill, named from what
 * it holds, saved, and sliding into the saved clips. A sample game: the page
 * shows no capture of League, so the video is the champion's splash, the
 * fallback the recordings page itself uses for a game without a thumbnail.
 */
const root = ref<HTMLElement | null>(null)
const { reached, playing } = useSceneBeats(root, [500, 1500, 900, 900, 700, 3600])

/** Where each moment sits on the video, as a fraction of its length. */
const KILLS = [{ at: 0.255, label: '×3' }, { at: 0.47 }, { at: 0.71, label: '×2' }]
const ASSISTS = [0.12, 0.33, 0.4, 0.58, 0.64, 0.83]
const DEATHS = [0.19, 0.55]
const OBJECTIVES = [
  { at: 0.3, icon: 'i-lucide-flame', side: 'ally' },
  { at: 0.5, icon: 'i-lucide-castle', side: 'enemy' },
  { at: 0.68, icon: 'i-lucide-skull', side: 'ally' },
  { at: 0.86, icon: 'i-lucide-castle', side: 'ally' },
] as const
const RANGE = { from: 0.235, to: 0.29 }

const playhead = computed(() => (reached(1) ? RANGE.from : 0.04))
const selecting = computed(() => reached(2))
const named = computed(() => reached(3))
const saved = computed(() => reached(4))

const CLIPS = [
  { title: 'Triple kill on Ahri', key: SAMPLE_ME, length: '0:24' },
  { title: 'Kill + Dragon', key: 'Vi', length: '0:31' },
  { title: 'Double kill', key: 'Jinx', length: '0:19' },
] as const
const splashOf = useCanonicalSplash()
const pct = (fraction: number) => `${(fraction * 100).toFixed(1)}%`
</script>

<template>
  <div ref="root" class="flex flex-col gap-4">
    <div class="overflow-hidden rounded-xl bg-ink-950 ring-1 ring-white/10 shadow-[0_30px_80px_-24px_rgb(0_0_0/0.9)]">
      <div class="relative aspect-[21/8] overflow-hidden">
        <div class="absolute inset-0 bg-cover bg-[50%_20%] brightness-[0.55]" :style="{ backgroundImage: `url(${splashOf(championSplashUrl(SAMPLE_ME))})` }" />
        <div class="absolute inset-x-0 bottom-0 flex items-center gap-3 bg-gradient-to-t from-black/80 to-transparent px-4 pb-3 pt-8 text-white/80">
          <UIcon name="i-lucide-play" class="size-4" />
          <UIcon name="i-lucide-skip-back" class="size-3.5" />
          <UIcon name="i-lucide-skip-forward" class="size-3.5" />
          <span class="stat-value text-xs text-white/80">{{ reached(1) ? '7:58' : '1:16' }} / 31:40</span>
          <span class="ml-auto text-[10px] font-medium text-white/60">Sample game</span>
        </div>
      </div>

      <!-- The timeline: objectives above, then the player's kills, assists and deaths on lanes of their own. -->
      <div class="relative px-4 pb-11 pt-3">
        <div class="relative ml-6 h-5">
          <UIcon
            v-for="objective in OBJECTIVES"
            :key="objective.at"
            :name="objective.icon"
            class="absolute top-0.5 size-3.5 -translate-x-1/2"
            :class="objective.side === 'ally' ? 'text-ally' : 'text-enemy'"
            :style="{ left: pct(objective.at) }"
          />
        </div>
        <div class="flex flex-col gap-1.5">
          <div class="flex items-center gap-2">
            <UIcon name="i-lucide-swords" class="size-3.5 w-4 shrink-0 text-primary" />
            <div class="relative h-4 flex-1 rounded bg-white/[0.03]">
              <span
                v-for="kill in KILLS"
                :key="kill.at"
                class="absolute top-0.5 flex h-3 -translate-x-1/2 items-center rounded-sm bg-primary px-1 text-[8px] font-bold leading-none text-inverted"
                :style="{ left: pct(kill.at) }"
              >{{ kill.label ?? '' }}</span>
            </div>
          </div>
          <div class="flex items-center gap-2">
            <UIcon name="i-lucide-handshake" class="size-3.5 w-4 shrink-0 text-muted" />
            <div class="relative h-4 flex-1 rounded bg-white/[0.03]">
              <span v-for="at in ASSISTS" :key="at" class="absolute top-1 size-2 -translate-x-1/2 rounded-full bg-white/50" :style="{ left: pct(at) }" />
            </div>
          </div>
          <div class="flex items-center gap-2">
            <UIcon name="i-lucide-skull" class="size-3.5 w-4 shrink-0 text-enemy/80" />
            <div class="relative h-4 flex-1 rounded bg-white/[0.03]">
              <span v-for="at in DEATHS" :key="at" class="absolute top-0.5 flex size-3 -translate-x-1/2 items-center justify-center rounded-sm bg-red-950 ring-1 ring-enemy/40" :style="{ left: pct(at) }">
                <UIcon name="i-lucide-skull" class="size-2 text-enemy" />
              </span>
            </div>
          </div>
        </div>

        <!-- The clip range, the playhead over everything. -->
        <div class="pointer-events-none absolute top-3 bottom-11 left-10 right-4">
          <div
            class="absolute inset-y-0 rounded transition-[width,background-color] duration-700 ease-out"
            :class="saved ? 'bg-stat-gold/25 ring-1 ring-stat-gold/70' : 'bg-primary/15 ring-1 ring-primary/60'"
            :style="{ left: pct(RANGE.from), width: selecting ? pct(RANGE.to - RANGE.from) : '0%' }"
          />
          <span
            class="absolute -bottom-8 -translate-x-1/2 whitespace-nowrap rounded-md bg-elevated px-2 py-1 text-[11px] font-medium text-highlighted ring-1 ring-white/10 transition-all duration-300"
            :class="named ? 'opacity-100' : 'translate-y-1 opacity-0'"
            :style="{ left: pct((RANGE.from + RANGE.to) / 2) }"
          >
            Triple kill on Ahri
            <span class="ml-1.5 rounded px-1.5 py-0.5 text-[10px] font-semibold transition-colors" :class="saved ? 'bg-stat-gold/20 text-stat-gold' : 'bg-primary text-inverted'">{{ saved ? 'Saved' : 'Save clip' }}</span>
          </span>
          <span class="absolute inset-y-0 w-px bg-highlighted transition-[left] duration-[1400ms] ease-in-out" :class="!playing && 'hidden'" :style="{ left: pct(playhead) }" />
        </div>
      </div>
    </div>

    <!-- Saved clips: the new one slides in at the head of the list. -->
    <p class="-mb-1 text-xs font-medium text-dimmed">Saved clips</p>
    <ul class="grid grid-cols-3 gap-3 lg:w-2/3">
      <li
        v-for="(clip, index) in CLIPS"
        :key="clip.title"
        class="landing-clip"
        :class="{ 'is-on': index > 0 || saved }"
      >
        <div class="relative aspect-video overflow-hidden rounded-lg bg-elevated ring-1 ring-white/5">
          <div class="size-full bg-cover bg-center brightness-75" :style="{ backgroundImage: `url(${splashOf(championSplashUrl(clip.key))})` }" />
          <span class="absolute bottom-1 right-1 rounded bg-black/70 px-1 text-[10px] tabular-nums text-white/90">{{ clip.length }}</span>
        </div>
        <p class="mt-1.5 flex items-center gap-1.5 truncate text-xs font-medium text-highlighted">
          <UIcon name="i-lucide-scissors" class="size-3 shrink-0 text-muted" />{{ clip.title }}
        </p>
      </li>
    </ul>
  </div>
</template>

<style scoped>
.landing-clip {
  opacity: 0;
  translate: -16px 0;
  transition: opacity 500ms cubic-bezier(0.2, 0, 0, 1), translate 500ms cubic-bezier(0.2, 0, 0, 1);
}

.landing-clip.is-on {
  opacity: 1;
  translate: 0 0;
}

@media (prefers-reduced-motion: reduce) {
  .landing-clip { transition: none; }
}
</style>
