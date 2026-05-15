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
      class="mt-2 flex flex-wrap items-center gap-2"
    >
      <template
        v-for="(key, index) in skillOrder.sequence"
        :key="`${key}-${index}`"
      >
        <div class="relative size-12">
          <NuxtImg
            v-if="spellByKey(key)"
            :src="spellByKey(key)!.iconUrl"
            :alt="spellByKey(key)!.name"
            :title="spellByKey(key)!.name"
            width="48"
            height="48"
            class="size-12 rounded-md"
          />
          <span
            v-else
            class="inline-flex size-12 items-center justify-center rounded-md border border-default text-sm"
          >
            {{ key }}
          </span>

          <span class="absolute bottom-0.5 left-1/2 inline-flex size-4 -translate-x-1/2 items-center justify-center rounded bg-default/85 text-[9px] font-bold uppercase ring-1 ring-default backdrop-blur-sm">
            {{ key }}
          </span>
        </div>

        <UIcon
          v-if="index < skillOrder.sequence.length - 1"
          name="i-lucide-chevron-right"
          class="size-4 text-dimmed"
        />
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
