<script setup lang="ts">
import type { SkillOrderOptionResponse } from '~~/shared/types/champions'
import type { ChampionStaticData } from '~~/shared/types/static-data'
import { formatPercentage } from '~~/shared/utils/ddragon'

const props = defineProps<{
  skillOrder: SkillOrderOptionResponse | null
  championStatic: ChampionStaticData
}>()

function spellByKey(key: string) {
  return props.championStatic.championSpells[key] ?? null
}
</script>

<template>
  <div>
    <h2 class="text-sm font-medium text-muted">
      Skill order
    </h2>
    <div
      v-if="skillOrder"
      class="mt-2 flex flex-wrap items-center gap-1"
    >
      <template
        v-for="(key, index) in skillOrder.sequence"
        :key="`${key}-${index}`"
      >
        <NuxtImg
          v-if="spellByKey(key)"
          :src="spellByKey(key)!.iconUrl"
          :alt="spellByKey(key)!.name"
          :title="`${key} — ${spellByKey(key)!.name}`"
          width="32"
          height="32"
          class="size-8 rounded"
        />
        <span
          v-else
          class="inline-flex size-8 items-center justify-center rounded border border-default text-xs"
        >
          {{ key }}
        </span>
      </template>
      <span class="ml-2 text-sm text-muted">
        {{ formatPercentage(skillOrder.winRate) }} WR · {{ skillOrder.games }} games
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
