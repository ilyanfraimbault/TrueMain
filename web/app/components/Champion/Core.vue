<script setup lang="ts">
import type { ChampionCoreResponse, RunePageOptionResponse } from '~~/shared/types/champions'
import type { ChampionStaticData, RuneTreeResponse } from '~~/shared/types/static-data'

defineProps<{
  core: ChampionCoreResponse | null
  championStatic: ChampionStaticData
  topRunePage: RunePageOptionResponse | null
  runeTree: RuneTreeResponse | null
}>()
</script>

<template>
  <UCard>
    <div class="grid gap-x-6 gap-y-5 lg:grid-cols-[minmax(0,1fr)_max-content]">
      <div class="flex flex-col justify-between gap-5">
        <div class="grid gap-4 sm:grid-cols-3">
          <ChampionCoreSpells
            :summoners="core?.summonerSpells ?? null"
            :champion-static="championStatic"
          />
          <ChampionCoreSkillOrder
            :skill-order="core?.skillOrder ?? null"
            :champion-static="championStatic"
            class="sm:col-span-2"
          />
        </div>
        <div class="grid gap-4 sm:grid-cols-3">
          <ChampionCoreStarterItems
            :starter="core?.starterItems ?? null"
            :champion-static="championStatic"
          />
          <ChampionCoreBoots
            :boots="core?.boots ?? null"
            :champion-static="championStatic"
          />
          <ChampionCoreBuildPath
            :path="core?.buildPath ?? null"
            :champion-static="championStatic"
          />
        </div>
      </div>
      <div v-if="topRunePage && runeTree">
        <ChampionCoreRunes
          :page="topRunePage"
          :tree="runeTree"
        />
      </div>
    </div>
  </UCard>
</template>
