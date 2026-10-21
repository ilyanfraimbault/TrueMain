<script setup lang="ts">
import type { StaticItemData } from '#shared/types/static-data'
import type { BuildPath } from '~/utils/build-path'

/**
 * The build path (#2183): the next item's components in the order the
 * champion's mains buy them, each owned, bought now with the gold in hand, or
 * later. An intermediate being started (Lost Chapter) opens into its own parts
 * under its small icon. Under the next item on the game page's panel and on
 * the overlay, both from `useNextItemPanel`.
 */
withDefaults(defineProps<{ path: BuildPath, statics: Record<number, StaticItemData>, size?: number }>(), { size: 24 })
</script>

<template>
  <ol v-if="path.entries.length" class="flex flex-wrap items-start gap-1.5" aria-label="Build path">
    <li v-for="(entry, index) in path.entries" :key="`${entry.itemId}-${index}`">
      <div
        v-if="entry.parts.length"
        class="relative flex gap-1 rounded-md px-1 pt-1.5 pb-0.5 ring-1"
        :class="entry.state === 'buy' ? 'bg-primary/5 ring-primary/30' : 'ring-default'"
        :aria-label="statics[entry.itemId]?.name"
      >
        <GameTooltipItemIcon
          :item="statics[entry.itemId] ?? null"
          :width="12"
          :height="12"
          class="absolute -top-1.5 left-1 size-3 rounded-sm ring-1 ring-default"
        />
        <GameBuildPathTile v-for="(part, partIndex) in entry.parts" :key="`${part.itemId}-${partIndex}`" :tile="part" :statics="statics" :size="size" />
      </div>
      <GameBuildPathTile v-else :tile="entry" :statics="statics" :size="size" />
    </li>
  </ol>
</template>
