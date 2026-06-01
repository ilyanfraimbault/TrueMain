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
  <div>
    <h2 class="text-sm font-medium text-muted">
      Skill order
    </h2>
    <!-- Fixed from sm: 4 icons × 36 px + 3 chevrons × 16 px + 6 gaps × 4 px = 216 px wide,
         36 px tall. Width locks at the 4-key worst case so the A2a row never
         shifts when builds have fewer keys. No-flex-wrap + overflow-hidden clips
         any unexpected overflow rather than reflowing the layout. Mobile stays
         fluid (w-full). -->
    <div class="mt-2 flex h-9 w-full shrink-0 items-center gap-1 overflow-hidden sm:w-[216px]">
      <template v-if="skillOrder">
        <template
          v-for="(key, index) in skillOrder.sequence"
          :key="`${key}-${index}`"
        >
          <div class="relative size-9 shrink-0">
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
            class="size-4 shrink-0 text-dimmed"
          />
        </template>
      </template>
      <span
        v-else
        class="text-sm text-muted"
      >
        No data
      </span>
    </div>
  </div>
</template>
