<script setup lang="ts">
import { computed } from 'vue'
import type { StaticItemData } from '~~/shared/types/static-data'
import { parseItemDescription } from '~~/shared/utils/tooltip-parser'

const props = defineProps<{
  item: StaticItemData
  /** Optional pickrate (0..1) — only passed from the build-tree call site to surface slot popularity. */
  pickRate?: number
}>()

const parsed = computed(() => props.item.description ? parseItemDescription(props.item.description) : [])
const hasDescription = computed(() => parsed.value.length > 0)
const goldLabel = computed(() => props.item.totalGold > 0 ? `${props.item.totalGold.toLocaleString('en-US')}g` : null)
const pickRateLabel = computed(() => {
  if (props.pickRate === undefined || props.pickRate === null) return null
  const pct = props.pickRate * 100
  // Sub-1% picks would otherwise round to "0%" and look broken; show one decimal there.
  return pct >= 1 ? `${pct.toFixed(0)}%` : `${pct.toFixed(1)}%`
})
</script>

<template>
  <div>
    <header class="mb-2 flex items-center gap-3">
      <SkeletonImage
        :src="item.iconUrl"
        :alt="item.name"
        :width="36"
        :height="36"
        class="size-9 shrink-0 rounded"
      />
      <div class="min-w-0 flex-1">
        <div class="truncate font-semibold text-default">
          {{ item.name }}
        </div>
        <div
          v-if="goldLabel"
          class="text-xs text-stat-active"
        >
          {{ goldLabel }}
        </div>
      </div>
      <div
        v-if="pickRateLabel"
        class="shrink-0 self-start text-xs font-semibold text-muted"
      >
        {{ pickRateLabel }} pick
      </div>
    </header>
    <div class="border-t border-default/40 pt-2 text-sm">
      <GameTooltipRichText
        v-if="hasDescription"
        :segments="parsed"
      />
      <p
        v-else-if="item.plaintext"
        class="text-muted"
      >
        {{ item.plaintext }}
      </p>
    </div>
  </div>
</template>
