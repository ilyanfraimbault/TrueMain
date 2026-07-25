<script setup lang="ts">
import type { BuildChoice } from '~~/shared/types/divergence'
import type { ChampionStaticData, StaticItemData } from '~~/shared/types/static-data'
import { formatPercentage } from '~~/shared/utils/ddragon'

const props = defineProps<{
  /** Which side of the comparison this column is. */
  label: string
  choice: BuildChoice
  /** Wording for the share line: "of your games" / "of mains games". */
  shareSuffix: string
  itemsMap: Record<number, StaticItemData>
  championStatic: ChampionStaticData | null
  /** Chevrons between the icons — for ordered choices (item path, skill order). */
  ordered?: boolean
  /** Emphasise this column (the mains' pick, when the player diverges from it). */
  highlight?: boolean
}>()

const items = computed<StaticItemData[]>(() =>
  props.choice.itemIds
    .map(id => props.itemsMap[id])
    .filter((item): item is StaticItemData => Boolean(item)),
)

const skills = computed(() => props.choice.skills)

function spellByKey(key: string) {
  return props.championStatic?.championSpells[key] ?? null
}
</script>

<template>
  <div
    class="flex min-w-0 flex-col gap-2 rounded-md p-2"
    :class="highlight ? 'bg-primary/8 ring-1 ring-primary/25' : 'bg-elevated/30'"
  >
    <p class="text-[0.6875rem] font-semibold uppercase tracking-wide text-muted">
      {{ label }}
    </p>

    <!-- Icons for whichever kind of choice this dimension carries: item ids for
         starter / boots / core path, Q-W-E keys for the skill order. Exactly one
         of the two is ever populated (see the BuildChoice contract). -->
    <div class="flex h-9 flex-wrap items-center gap-1">
      <template
        v-for="(item, index) in items"
        :key="`item-${item.id}-${index}`"
      >
        <GameTooltipItemIcon
          :item="item"
          :width="36"
          :height="36"
          class="size-9 shrink-0 rounded"
        />
        <UIcon
          v-if="ordered && index < items.length - 1"
          name="i-lucide-chevron-right"
          class="size-4 shrink-0 text-dimmed"
        />
      </template>

      <template
        v-for="(key, index) in skills"
        :key="`skill-${key}-${index}`"
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
          v-if="ordered && index < skills.length - 1"
          name="i-lucide-chevron-right"
          class="size-4 shrink-0 text-dimmed"
        />
      </template>

      <span
        v-if="!items.length && !skills.length"
        class="text-sm text-muted"
      >
        No data
      </span>
    </div>

    <p class="text-xs text-muted">
      <span class="font-medium text-default">{{ formatPercentage(choice.pickRate) }}</span>
      {{ shareSuffix }}
      <span class="text-dimmed">·</span>
      {{ formatPercentage(choice.winRate) }} win over {{ choice.games }}
      {{ choice.games === 1 ? 'game' : 'games' }}
    </p>
  </div>
</template>
