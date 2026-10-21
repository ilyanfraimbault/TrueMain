<script setup lang="ts">
import { SAMPLE_CLOCK, SAMPLE_OBJECTIVES } from '~/utils/desktop-landing-sample'

/**
 * The overlay's objectives strip, redrawn for `/download` the way the app draws
 * it (`desktop/app/app/components/overlay/OverlayObjectives.vue`, #2078, #2184):
 * a pill per camp still to come — the game's minimap icon and its countdown, grey
 * while it waits on the camp before it — then the soul race under it, each side's
 * drakes meeting at the pit's next one. The sample's clock does not run: the page
 * shows one moment of the game.
 */
const canonicalIcon = useCanonicalIcon()
const MINIMAP = 'https://raw.communitydragon.org/latest/game/assets/ux/minimap/icons'
const icon = (name: string) => canonicalIcon(`${MINIMAP}/${name}.png`)

const countdown = (at: number) => {
  const seconds = Math.max(0, at - SAMPLE_CLOCK)
  return `${Math.floor(seconds / 60)}:${`${seconds % 60}`.padStart(2, '0')}`
}

const { drakes, next, spawns } = SAMPLE_OBJECTIVES
/** The grubs are gone; Baron waits on the Herald (`pillStatus`'s rule). */
const PILLS = [
  { key: 'dragon', icon: `dragon_${next}`, at: spawns.dragon, waiting: false },
  { key: 'herald', icon: 'riftherald', at: spawns.herald, waiting: false },
  { key: 'baron', icon: 'baron', at: spawns.baron, waiting: true },
] as const
</script>

<template>
  <div class="flex flex-col gap-1">
    <ol class="flex h-6 items-center justify-center gap-1">
      <li
        v-for="pill in PILLS"
        :key="pill.key"
        :data-objective="pill.key"
        class="flex h-6 w-16 items-center justify-center gap-1 rounded-full ring-1 ring-inset"
        :class="pill.waiting ? 'bg-elevated/40 ring-transparent' : 'bg-elevated/70 ring-default'"
      >
        <img :src="icon(pill.icon)" alt="" width="16" height="16" class="size-4 shrink-0" :class="{ 'opacity-45 grayscale': pill.waiting }">
        <span class="text-[11px] leading-none tabular-nums" :class="pill.waiting ? 'text-dimmed' : 'text-muted'">{{ countdown(pill.at) }}</span>
      </li>
    </ol>
    <div class="grid h-3.5 grid-cols-[1fr_auto_1fr] items-center gap-1" data-soul-race>
      <span class="flex items-center justify-end gap-0.5">
        <span class="h-px flex-1 bg-linear-to-r from-transparent to-ally/50" />
        <img v-for="(element, index) in drakes.ally" :key="index" :src="icon(`dragon_${element}`)" alt="" width="14" height="14" class="size-3.5 rounded-full ring-1 ring-ally/70">
      </span>
      <img :src="icon(`dragon_${next}`)" alt="" width="14" height="14" class="size-3.5">
      <span class="flex items-center gap-0.5">
        <img v-for="(element, index) in drakes.enemy" :key="index" :src="icon(`dragon_${element}`)" alt="" width="14" height="14" class="size-3.5 rounded-full ring-1 ring-enemy/70">
        <span class="h-px flex-1 bg-linear-to-r from-enemy/50 to-transparent" />
      </span>
    </div>
  </div>
</template>
