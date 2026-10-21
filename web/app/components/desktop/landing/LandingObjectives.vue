<script setup lang="ts">
import { SAMPLE_CLOCK, SAMPLE_OBJECTIVES } from '~/utils/desktop-landing-sample'

/**
 * The overlay's objectives panel, redrawn for `/download` the way the app draws
 * it (`desktop/app/app/components/overlay/OverlayObjectives.vue`, #2078): the
 * game's minimap icons, the dragon pit between the drakes each side took with
 * its countdown under it, then a row for each camp still to come. The sample's
 * clock does not run: the page shows one moment of the game.
 */
const canonicalIcon = useCanonicalIcon()
const MINIMAP = 'https://raw.communitydragon.org/latest/game/assets/ux/minimap/icons'
const icon = (name: string) => canonicalIcon(`${MINIMAP}/${name}.png`)

const countdown = (at: number) => {
  const seconds = Math.max(0, at - SAMPLE_CLOCK)
  return `${Math.floor(seconds / 60)}:${`${seconds % 60}`.padStart(2, '0')}`
}

const { drakes, next, spawns } = SAMPLE_OBJECTIVES
const CAMPS = [
  { key: 'herald', icon: 'riftherald', at: spawns.herald },
  { key: 'baron', icon: 'baron', at: spawns.baron },
] as const
</script>

<template>
  <div class="flex flex-col gap-1.5">
    <p class="flex items-center gap-1 text-[10px] font-semibold uppercase tracking-wider text-dimmed">
      <DesktopLandingMark class="size-2.5" /> Objectives
    </p>
    <ul class="flex flex-col gap-1">
      <li class="grid h-7.5 grid-cols-[1fr_auto_1fr] items-center gap-1.5 rounded bg-elevated/60 px-1.5">
        <span class="flex items-center gap-0.5">
          <img v-for="(element, index) in drakes.ally" :key="index" :src="icon(`dragon_${element}`)" alt="" width="14" height="14" class="size-3.5 rounded-full ring-1 ring-ally/70">
        </span>
        <span class="flex flex-col items-center">
          <img :src="icon(`dragon_${next}`)" alt="" width="16" height="16" class="size-4">
          <span class="text-[10px] leading-none tabular-nums text-muted">{{ countdown(spawns.dragon) }}</span>
        </span>
        <span class="flex items-center justify-end gap-0.5">
          <img v-for="(element, index) in drakes.enemy" :key="index" :src="icon(`dragon_${element}`)" alt="" width="14" height="14" class="size-3.5 rounded-full ring-1 ring-enemy/70">
        </span>
      </li>
      <li v-for="camp in CAMPS" :key="camp.key" class="flex h-7.5 items-center justify-between gap-1.5 rounded bg-elevated/60 px-1.5">
        <img :src="icon(camp.icon)" alt="" width="16" height="16" class="size-4">
        <span class="text-[11px] tabular-nums text-muted">{{ countdown(camp.at) }}</span>
      </li>
    </ul>
  </div>
</template>
