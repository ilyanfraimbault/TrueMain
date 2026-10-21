<script setup lang="ts">
import type { StaticItemData } from '#shared/types/static-data'
import { championIconUrl, plateGold, sampleLaneGold } from '~/utils/desktop-landing-sample'

/**
 * The lane-gold plates (#2186) as `/download` draws them while TAB is held: the
 * game's scoreboard rows sketched where the game draws them — five rows, each
 * a lane's two champions at the board's ends — and on each row's middle the
 * app's plate (`OverlayGoldPlate`): the item-gold gap and a chevron toward the
 * side ahead, ours blue, theirs red, a dash when even. The frame under it is not
 * a TAB screen, so the rows stand in for the board the plates sit on.
 *
 * Fills its box: `LandingGameScreen` places it on the rows' rectangle
 * (`utils/scoreboard-geometry.ts` at a HUD scale of about 0.3: 309 → 693 px
 * down and 384 → 1536 px across a 1920 × 1080 screen). Plates scale like the
 * panels (`--landing-scale`).
 */
const props = defineProps<{ patch: string | null, items: Record<number, StaticItemData> | null }>()

const canonicalIcon = useCanonicalIcon()
const iconUrl = (key: string) => championIconUrl(key, props.patch)

const rows = computed(() => sampleLaneGold(props.items))
const ahead = (index: number) => {
  const lane = rows.value?.[index]
  if (!lane || lane.ours === lane.theirs) return null
  return lane.ours > lane.theirs ? 'left' : 'right'
}
const gap = (index: number) => {
  const lane = rows.value?.[index]
  return lane ? Math.abs(lane.ours - lane.theirs) : 0
}
</script>

<template>
  <ol class="flex size-full flex-col">
    <li v-for="(lane, index) in rows ?? []" :key="lane.ally" class="relative flex min-h-0 flex-1 items-center justify-between border-b border-white/5 bg-black/40 px-[1%]" :data-row="index">
      <img v-if="patch" :src="canonicalIcon(iconUrl(lane.ally))" alt="" width="32" height="32" class="aspect-square h-3/4 w-auto rounded-sm ring-1 ring-ally/40">
      <div class="landing-plate absolute left-1/2 top-1/2">
        <div
          class="flex h-5 min-w-12 items-center justify-center gap-0.5 rounded-md bg-neutral-950/80 px-1.5 text-[11px] leading-none font-semibold tabular-nums shadow-sm ring-1 ring-white/10 backdrop-blur-sm"
          :class="ahead(index) === null ? 'text-white/50' : ahead(index) === 'left' ? 'text-ally' : 'text-enemy'"
          :data-ahead="ahead(index) ?? 'even'"
        >
          <template v-if="ahead(index)">
            <UIcon v-if="ahead(index) === 'left'" name="i-lucide-chevron-left" class="size-3 shrink-0" />
            <span>{{ plateGold(gap(index)) }}</span>
            <UIcon v-if="ahead(index) === 'right'" name="i-lucide-chevron-right" class="size-3 shrink-0" />
          </template>
          <UIcon v-else name="i-lucide-minus" class="size-3" />
        </div>
      </div>
      <img v-if="patch" :src="canonicalIcon(iconUrl(lane.enemy))" alt="" width="32" height="32" class="aspect-square h-3/4 w-auto rounded-sm ring-1 ring-enemy/40">
    </li>
  </ol>
</template>

<style scoped>
.landing-plate {
  translate: -50% -50%;
  scale: var(--landing-scale, 0.6);
}
</style>
