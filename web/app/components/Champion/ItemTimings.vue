<script setup lang="ts">
import type { ChampionItemTiming } from '~~/shared/types/champions'
import type { StaticItemData } from '~~/shared/types/static-data'

const props = withDefaults(defineProps<{
  timings: ChampionItemTiming[]
  itemsMap: Record<number, StaticItemData>
  loading?: boolean
}>(), {
  loading: false,
})

// The endpoint returns every purchased item (potions, wards, components included).
// The spike view is about the items that actually come online, so keep boots-tier
// (~1100g) and up and drop the cheap noise. Cap the strip at a readable length.
const MIN_GOLD = 1100
const MAX_ITEMS = 10

interface TimedItem {
  item: StaticItemData
  avgSeconds: number
  games: number
}

const items = computed<TimedItem[]>(() =>
  props.timings
    .map(timing => ({
      item: props.itemsMap[timing.itemId],
      avgSeconds: timing.avgSeconds,
      games: timing.games,
    }))
    .filter((entry): entry is TimedItem => Boolean(entry.item) && entry.item.totalGold >= MIN_GOLD)
    .slice(0, MAX_ITEMS),
)

const hasData = computed(() => items.value.length > 0)

function formatGameTime(seconds: number): string {
  const total = Math.round(seconds)
  const minutes = Math.floor(total / 60)
  const remainder = total % 60
  return `${minutes}:${remainder.toString().padStart(2, '0')}`
}
</script>

<template>
  <section class="flex flex-col gap-4">
    <header class="flex flex-col gap-0.5">
      <h2 class="text-sm font-semibold">
        Power spikes
      </h2>
      <p class="text-xs text-muted">
        Average time each item is completed, earliest first.
      </p>
    </header>

    <USkeleton
      v-if="loading"
      class="h-20 w-full rounded-lg"
    />

    <p
      v-else-if="!hasData"
      class="glass rounded-lg px-4 py-8 text-center text-sm text-muted"
    >
      No item-timing data yet for this champion and lane.
    </p>

    <div
      v-else
      class="flex flex-wrap gap-3"
    >
      <div
        v-for="entry in items"
        :key="entry.item.id"
        class="flex flex-col items-center gap-1"
      >
        <GameTooltipItemIcon
          :item="entry.item"
          :width="36"
          :height="36"
          class="size-9 rounded"
        />
        <span class="text-xs font-medium tabular-nums text-muted">
          {{ formatGameTime(entry.avgSeconds) }}
        </span>
      </div>
    </div>
  </section>
</template>
