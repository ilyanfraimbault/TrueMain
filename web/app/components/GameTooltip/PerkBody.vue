<script setup lang="ts">
import { computed } from 'vue'
import type { StaticPerkData } from '~~/shared/types/static-data'
import { parseRuneDescription } from '~~/shared/utils/tooltip-parser'

const props = defineProps<{
  perk: StaticPerkData
}>()

const parsedShort = computed(() => props.perk.shortDesc ? parseRuneDescription(props.perk.shortDesc) : [])
const parsedLong = computed(() => props.perk.longDesc ? parseRuneDescription(props.perk.longDesc) : [])
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
    <div class="space-y-2 border-t border-default/40 pt-2 text-sm">
      <GameTooltipRichText
        v-if="parsedShort.length"
        :segments="parsedShort"
      />
      <div
        v-if="parsedLong.length"
        class="text-xs text-muted"
      >
        <GameTooltipRichText :segments="parsedLong" />
      </div>
    </div>
  </div>
</template>
