<script setup lang="ts">
import { computed } from 'vue'
import type { StaticPerkData } from '~~/shared/types/static-data'
import { parseRuneDescription } from '~~/shared/utils/tooltip-parser'

const props = defineProps<{
  perk: StaticPerkData
}>()

// Prefer the detailed `longDesc` (carries melee/ranged chips + full numbers),
// fall back to `shortDesc` when longDesc is missing (stat shards). Showing
// both stacks two near-duplicate paragraphs and clutters the tooltip.
const parsed = computed(() => {
  const source = props.perk.longDesc ?? props.perk.shortDesc
  return source ? parseRuneDescription(source) : []
})
</script>

<template>
  <div>
    <header class="mb-2 flex items-center gap-3">
      <SkeletonImage
        :src="perk.iconUrl"
        :alt="perk.name"
        :width="36"
        :height="36"
        class="size-9 shrink-0 rounded-full"
      />
      <div class="font-semibold text-default">
        {{ perk.name }}
      </div>
    </header>
    <div class="border-t border-default/40 pt-2 text-sm leading-relaxed">
      <GameTooltipRichText
        v-if="parsed.length"
        :segments="parsed"
      />
    </div>
  </div>
</template>
