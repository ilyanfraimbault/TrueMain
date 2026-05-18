<script setup lang="ts">
import { computed } from 'vue'
import type { StaticItemData } from '~~/shared/types/static-data'
import { parseItemDescription } from '~~/shared/utils/tooltip-parser'

const props = defineProps<{
  item: StaticItemData
}>()

const parsed = computed(() => props.item.description ? parseItemDescription(props.item.description) : [])
const hasDescription = computed(() => parsed.value.length > 0)
const goldLabel = computed(() => props.item.totalGold > 0 ? `${props.item.totalGold.toLocaleString('en-US')}g` : null)
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
      <div class="min-w-0">
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
