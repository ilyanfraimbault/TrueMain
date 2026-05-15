<script setup lang="ts">
import type { SummonerSpellOptionResponse } from '~~/shared/types/champions'
import type { ChampionStaticData } from '~~/shared/types/static-data'
import { formatPercentage } from '~~/shared/utils/ddragon'

const props = defineProps<{
  summoners: SummonerSpellOptionResponse | null
  championStatic: ChampionStaticData
}>()

function summonerName(id: number): string {
  return props.championStatic.summonerSpells[id]?.name ?? `Spell ${id}`
}

function summonerIcon(id: number): string {
  return props.championStatic.summonerSpells[id]?.iconUrl ?? ''
}
</script>

<template>
  <div>
    <h2 class="text-sm font-medium text-muted">
      Summoner spells
    </h2>
    <div
      v-if="summoners"
      class="mt-2 flex items-center gap-1"
    >
      <template
        v-for="spellId in [summoners.spell1Id, summoners.spell2Id]"
        :key="`sum-${spellId}`"
      >
        <NuxtImg
          v-if="summonerIcon(spellId)"
          :src="summonerIcon(spellId)"
          :alt="summonerName(spellId)"
          :title="summonerName(spellId)"
          width="40"
          height="40"
          class="size-10 rounded"
        />
        <span
          v-else
          class="inline-flex size-10 items-center justify-center rounded border border-default text-xs"
          :title="summonerName(spellId)"
        >
          {{ summonerName(spellId) }}
        </span>
      </template>
      <span class="ml-2 text-sm text-muted">
        {{ formatPercentage(summoners.winRate) }} WR · {{ summoners.games }} games
      </span>
    </div>
    <p
      v-else
      class="text-sm text-muted"
    >
      No data
    </p>
  </div>
</template>
