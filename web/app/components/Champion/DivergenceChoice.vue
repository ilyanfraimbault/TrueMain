<script setup lang="ts">
import type { BuildChoice } from '~~/shared/types/divergence'
import type { ChampionStaticData, StaticItemData } from '~~/shared/types/static-data'
import { itemSlots } from '~~/shared/utils/build'
import { formatPercentage } from '~~/shared/utils/ddragon'

const props = defineProps<{
  /** Which side of the comparison this column is. */
  label: string
  choice: BuildChoice
  /** Wording for the share line: "of Faker's games" / "of mains games". */
  shareSuffix: string
  itemsMap: Record<number, StaticItemData>
  championStatic: ChampionStaticData | null
  /**
   * True while `championStatic` is still loading. This card renders as soon as
   * the divergence fetch resolves, which is independent of the champion-static
   * one, so the skill columns would otherwise spend that window showing their
   * 'Q'/'W' letter box — see `SkeletonImage`'s `pending`.
   */
  championStaticPending?: boolean
  /** Chevrons between the icons — for ordered choices (item path, skill order). */
  ordered?: boolean
  /** Emphasise this column (the mains' pick, when the player diverges from it). */
  highlight?: boolean
}>()

// Slots, not resolved items — see `itemSlots`. Filtering here let the column's
// own "No data" state fire while the item map was still loading, on a choice
// that carried ids all along.
const items = computed(() => itemSlots(props.choice.itemIds, props.itemsMap))

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
        v-for="(slot, index) in items"
        :key="`item-${slot.id}-${index}`"
      >
        <GameTooltipItemIcon
          :item="slot.item"
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
            :pending="championStaticPending"
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
