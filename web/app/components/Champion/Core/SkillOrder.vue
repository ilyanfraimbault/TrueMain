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
         sequence[0] is maxed first and gets the emerald (primary) emphasis.
         Width locks from sm at the 3-key worst case (3 × 48 px icons + 2 × 20 px
         chevrons + 4 × 8 px gaps = 216 px) so the A2a row never shifts when a
         build has fewer keys. Mobile stays fluid (w-full). -->
    <div class="mt-2 flex h-12 w-full shrink-0 items-center gap-2 overflow-hidden sm:w-[216px]">
      <template v-if="skillOrder && skillOrder.sequence.length">
        <template
          v-for="(key, index) in skillOrder.sequence"
          :key="`${key}-${index}`"
        >
          <div
            class="relative size-12 shrink-0 rounded-lg ring-1"
            :class="index === 0 ? 'bg-primary/10 ring-2 ring-primary' : 'ring-default'"
          >
            <GameTooltipChampionSpellIcon
              :spell="spellByKey(key)"
              :fallback-label="key"
              :width="48"
              :height="48"
              class="size-12 rounded-lg"
            />
            <!-- Priority ordinal, top-left: 1 = maxed first. -->
            <span
              class="absolute -left-1 -top-1 inline-flex size-4 items-center justify-center rounded-full text-[10px] font-bold tabular-nums ring-1 ring-default"
              :class="index === 0 ? 'bg-primary text-inverted' : 'bg-default text-muted'"
            >
              {{ index + 1 }}
            </span>
            <!-- Skill key (Q/W/E), bottom-centre. -->
            <span
              class="absolute bottom-0 left-1/2 inline-flex h-4 min-w-4 -translate-x-1/2 items-center justify-center rounded px-1 text-[10px] font-bold uppercase ring-1 backdrop-blur-sm"
              :class="index === 0
                ? 'bg-primary text-inverted ring-primary'
                : 'bg-default/85 text-default ring-default'"
            >
              {{ key }}
            </span>
          </div>
          <UIcon
            v-if="index < skillOrder.sequence.length - 1"
            name="i-lucide-chevron-right"
            class="size-5 shrink-0 text-dimmed"
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
