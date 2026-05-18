<script setup lang="ts">
import { computed } from 'vue'
import type { StaticSummonerSpellData } from '~~/shared/types/static-data'
import { parseSummonerSpell } from '~~/shared/utils/tooltip-parser'

const props = defineProps<{
  spell: StaticSummonerSpellData
}>()

const parsed = computed(() => props.spell.description ? parseSummonerSpell(props.spell.description) : [])
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
      <div class="font-semibold text-default">
        {{ spell.name }}
      </div>
    </header>
    <div class="space-y-2 border-t border-default/40 pt-2 text-sm">
      <GameTooltipRichText
        v-if="parsed.length"
        :segments="parsed"
      />
      <div
        v-if="spell.cooldown"
        class="text-sm"
      >
        <span class="font-semibold">Cooldown:</span> {{ spell.cooldown }}s
      </div>
    </div>
  </div>
</template>
