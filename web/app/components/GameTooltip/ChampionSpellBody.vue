<script setup lang="ts">
import { computed } from 'vue'
import type { StaticChampionSpellData } from '~~/shared/types/static-data'
import { parseChampionSpell } from '~~/shared/utils/tooltip-parser'

const props = defineProps<{
  spell: StaticChampionSpellData
}>()

const parsed = computed(() => props.spell.description ? parseChampionSpell(props.spell.description) : [])
const showsCost = computed(() => Boolean(props.spell.costBurn && props.spell.costType && props.spell.costType.trim().toLowerCase() !== 'no cost'))
</script>

<template>
  <div>
    <header class="mb-2 flex items-center gap-3">
      <SkeletonImage
        :src="spell.iconUrl"
        :alt="spell.name"
        :width="36"
        :height="36"
        class="size-9 shrink-0 rounded"
      />
      <div class="min-w-0">
        <div class="flex items-baseline gap-2 font-semibold text-default">
          <span class="text-xs uppercase tracking-wider text-stat-active">{{ spell.key }}</span>
          <span class="truncate">{{ spell.name }}</span>
        </div>
        <div
          v-if="spell.cooldownBurn || showsCost || spell.rangeBurn"
          class="text-xs text-muted"
        >
          <template v-if="spell.cooldownBurn">
            Cooldown: <span class="text-stat-speed font-semibold">{{ spell.cooldownBurn }}s</span>
          </template>
          <template v-if="showsCost">
            <span v-if="spell.cooldownBurn"> · </span>
            Cost: <span class="text-stat-mana font-semibold">{{ spell.costBurn }} {{ spell.costType?.trim() }}</span>
          </template>
          <template v-if="spell.rangeBurn">
            <span v-if="spell.cooldownBurn || showsCost"> · </span>
            Range: <span class="text-default font-semibold">{{ spell.rangeBurn }}</span>
          </template>
        </div>
      </div>
    </header>
    <div class="border-t border-default/40 pt-2 text-sm">
      <GameTooltipRichText
        v-if="parsed.length"
        :segments="parsed"
      />
    </div>
  </div>
</template>
