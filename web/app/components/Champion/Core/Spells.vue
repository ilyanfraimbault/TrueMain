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
  <SectionCard title="Summoners">
    <div
      v-if="summoners"
      class="flex items-center gap-1"
    >
      <GameTooltipSummonerSpellIcon
        v-for="spellId in [summoners.spell1Id, summoners.spell2Id]"
        :key="`sum-${spellId}`"
        :spell="summonersMap[spellId] ?? null"
        :fallback-label="summonerName(spellId)"
        :width="36"
        :height="36"
        class="size-9 rounded"
      />
    </div>
    <p
      v-else
      class="text-sm text-muted"
    >
      No data
    </p>
  </SectionCard>
</template>
