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
    <!-- The aggregate only carries the max-priority of the three basic spells
         (which of Q/W/E to level first), so `sequence` is at most 3 keys — never
         a per-level 1-18 grid. We render them left-to-right as a priority chain:
         sequence[0] is maxed first. Plain icons + Q/W/E badge + chevron, matching
         the skill-order rendering in the variations panel — no bordered tiles.
         Fixed from sm: 3 icons × 36 px + 2 chevrons × 16 px + 4 gaps × 4 px =
         156 px wide, 36 px tall, so the A2a row never shifts when a build has
         fewer keys. Mobile stays fluid (w-full). -->
    <div class="mt-2 flex h-9 w-full shrink-0 items-center gap-1 overflow-hidden sm:w-[156px]">
      <template v-if="skillOrder && skillOrder.sequence.length">
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
            <ItemRankBadge :value="key" />
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
