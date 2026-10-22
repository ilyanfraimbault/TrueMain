<script setup lang="ts">
import type { StaticItemData } from '#shared/types/static-data'
import { SAMPLE_BUILD_ORDER, SAMPLE_BUILD_PATH, SAMPLE_NEXT_ITEM } from '~/utils/desktop-landing-sample'

/**
 * The overlay's build path panel, redrawn for `/download` the way the app draws
 * it (`desktop/app/app/components/overlay/OverlayNextItem.vue`, #2183): the next
 * item and the gold still to earn, its components in the order the mains buy
 * them — held (dimmed and checked), bought now (ringed, its price in the accent)
 * or later (muted) — then the whole build as a row (#2101).
 */
const props = defineProps<{ patch: string | null, items: Record<number, StaticItemData> | null }>()

const canonicalIcon = useCanonicalIcon()
const itemUrl = (id: number) => (props.patch ? `https://ddragon.leagueoflegends.com/cdn/${props.patch}/img/item/${id}.png` : null)
const name = computed(() => props.items?.[SAMPLE_NEXT_ITEM.itemId]?.name ?? 'Infinity Edge')
</script>

<template>
  <div class="space-y-1.5">
    <div class="flex items-center gap-2">
      <img v-if="patch" :src="canonicalIcon(itemUrl(SAMPLE_NEXT_ITEM.itemId))" alt="" width="28" height="28" class="size-7 shrink-0 rounded-md ring-1 ring-primary/60">
      <span v-else class="size-7 shrink-0 rounded-md bg-elevated" />
      <div class="min-w-0 flex-1">
        <p class="flex items-center gap-1 text-[10px] font-semibold uppercase tracking-wider text-dimmed">
          <DesktopLandingMark class="size-2.5" /> Next item
          <span class="ml-auto normal-case tracking-normal tabular-nums text-muted"><span class="text-stat-gold">{{ formatCount(SAMPLE_NEXT_ITEM.missing) }}</span> to go</span>
        </p>
        <p class="truncate text-[13px] font-semibold leading-tight text-highlighted">{{ name }}</p>
      </div>
    </div>

    <ol class="flex items-start gap-1.5" data-build-path>
      <li v-for="tile in SAMPLE_BUILD_PATH" :key="tile.itemId" :data-state="tile.state" class="flex flex-col items-center gap-0.5">
        <div class="relative">
          <img
            v-if="patch"
            :src="canonicalIcon(itemUrl(tile.itemId))"
            alt=""
            width="20"
            height="20"
            class="size-5 rounded"
            :class="{ 'opacity-40 grayscale': tile.state === 'owned', 'ring-2 ring-primary': tile.state === 'buy', 'opacity-55': tile.state === 'later' }"
          >
          <span v-else class="block size-5 rounded bg-elevated" />
          <span v-if="tile.state === 'owned'" class="absolute -right-1 -bottom-1 flex size-3 items-center justify-center rounded-full bg-default ring-1 ring-primary/50">
            <UIcon name="i-lucide-check" class="size-2 text-primary" />
          </span>
        </div>
        <!-- A held part prints no price; its empty line keeps the tiles level. -->
        <span class="h-2.5 text-[10px] leading-none tabular-nums" :class="tile.state === 'buy' ? 'font-semibold text-primary' : 'text-dimmed'">{{ tile.state === 'owned' ? '' : tile.cost }}</span>
      </li>
    </ol>

    <!-- The build row (`GameBuildOrder`, #2101): done dimmed, the next ringed, the rest to come. -->
    <div class="flex items-center gap-1" data-build-row>
      <template v-for="step in SAMPLE_BUILD_ORDER" :key="step.itemId">
        <img
          v-if="patch"
          :src="canonicalIcon(itemUrl(step.itemId))"
          alt=""
          width="18"
          height="18"
          class="size-[18px] rounded"
          :class="{ 'opacity-35 grayscale': step.state === 'Completed', 'ring-2 ring-primary': step.state === 'Next', 'opacity-80': step.state === 'Upcoming' }"
        >
        <span v-else class="block size-[18px] rounded bg-elevated" />
      </template>
    </div>
  </div>
</template>
