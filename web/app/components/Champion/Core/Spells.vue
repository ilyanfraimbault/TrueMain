<script setup lang="ts">
import type { BuildSummonerSpells } from '~~/shared/types/champions'
import type { StaticSummonerSpellData } from '~~/shared/types/static-data'

const props = defineProps<{
  summoners: BuildSummonerSpells | null
  summonersMap: Record<number, StaticSummonerSpellData>
}>()

function summonerName(id: number): string {
  return props.summonersMap[id]?.name ?? `Spell ${id}`
}
</script>

<template>
  <div>
    <h2 class="text-sm font-medium text-muted">
      Summoners
    </h2>
    <!-- Fixed: 2 spells × 36 px + 1 gap × 4 px = 76 px wide, 36 px tall.
         Height and width are pinned so the A1 column never shifts when data
         is absent — the "No data" state occupies the same box. -->
    <div class="mt-2 flex h-9 w-[76px] shrink-0 items-center gap-1 overflow-hidden">
      <template v-if="summoners">
        <GameTooltipSummonerSpellIcon
          v-for="spellId in [summoners.spell1Id, summoners.spell2Id]"
          :key="`sum-${spellId}`"
          :spell="summonersMap[spellId] ?? null"
          :fallback-label="summonerName(spellId)"
          :width="36"
          :height="36"
          class="size-9 shrink-0 rounded"
        />
      </template>
    </div>
  </div>
</template>
