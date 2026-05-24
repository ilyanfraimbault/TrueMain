<script setup lang="ts">
import type { BuildSkillOrder } from '~~/shared/types/champions'
import type { ChampionStaticData } from '~~/shared/types/static-data'

const props = defineProps<{
  skillOrder: BuildSkillOrder | null
  championStatic: ChampionStaticData
}>()

function spellByKey(key: string) {
  return props.championStatic.championSpells[key] ?? null
}
</script>

<template>
  <SectionCard title="Skill order">
    <div
      v-if="skillOrder"
      class="flex flex-wrap items-center gap-1"
    >
      <template
        v-for="(key, index) in skillOrder.sequence"
        :key="`${key}-${index}`"
      >
        <div class="relative size-9">
          <GameTooltipChampionSpellIcon
            :spell="spellByKey(key)"
            :fallback-label="key"
            :width="36"
            :height="36"
            class="size-9 rounded"
          />
          <span class="absolute bottom-0 left-1/2 inline-flex h-3 min-w-3 -translate-x-1/2 items-center justify-center rounded bg-default/85 px-0.5 text-[8px] font-bold uppercase ring-1 ring-default backdrop-blur-sm">
            {{ key }}
          </span>
        </div>
        <UIcon
          v-if="index < skillOrder.sequence.length - 1"
          name="i-lucide-chevron-right"
          class="size-4 text-dimmed"
        />
      </template>
    </div>
    <p
      v-else
      class="text-sm text-muted"
    >
      No data
    </p>
  </SectionCard>
</template>
