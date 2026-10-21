<script setup lang="ts">
import type { LanePlate } from '~/utils/lane-gold'
import { plateGold } from '~/utils/lane-gold'

/**
 * One lane-gold plate (#2186): the gap between a scoreboard row's two
 * players, a chevron pointing at the side ahead, in that side's colour — ours
 * blue, theirs red. Dark whatever the app's theme: it sits on the game's own
 * dark scoreboard. Even reads as a dimmed dash.
 */
const props = defineProps<{ plate: LanePlate }>()

const tone = computed(() => (props.plate.ahead === null ? 'text-white/50' : props.plate.ours ? 'text-ally' : 'text-enemy'))
</script>

<template>
  <div
    class="flex h-5 min-w-12 items-center justify-center gap-0.5 rounded-md bg-neutral-950/80 px-1.5 text-[11px] leading-none font-semibold tabular-nums shadow-sm ring-1 ring-white/10 backdrop-blur-sm"
    :class="tone"
    :data-ahead="plate.ahead ?? 'even'"
  >
    <template v-if="plate.ahead">
      <UIcon v-if="plate.ahead === 'left'" name="i-lucide-chevron-left" class="size-3 shrink-0" />
      <span>{{ plateGold(plate.gold) }}</span>
      <UIcon v-if="plate.ahead === 'right'" name="i-lucide-chevron-right" class="size-3 shrink-0" />
    </template>
    <UIcon v-else name="i-lucide-minus" class="size-3" />
  </div>
</template>
