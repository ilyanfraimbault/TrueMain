<script setup lang="ts">
import type { MatchDetailSkillEvent } from '#shared/types/match-detail'
import { isLoadingStatus } from '#common/utils/async-data'

// The selected player's skill order: one column per level (chronological), one
// row per slot, each row headed by the real spell icon with its Q/W/E/R badge.
const props = defineProps<{
  championId: number
  skillEvents: MatchDetailSkillEvent[]
}>()

// Q/W/E/R icons at the latest patch — kit icons are stable across patches, so
// the match's own gameVersion isn't threaded through just to pick an image.
// Cached per champion, so re-selecting a player never re-downloads. Until it
// lands the icons show their skeleton, not the 'Q'/'W' letter fallback, which
// reads as a broken image and duplicates the key badge beside it.
const { data: championStatic, status } = useChampionStatic(() => props.championId, () => null)
const pending = computed(() => isLoadingStatus(status.value))

// One fill per slot, all four from the house ramp: the rose-gold ladder
// descends Q → W → E and the ultimate caps it in `--color-gold` (the MVP
// crown's token). All four stay light enough for the same dark-ink text.
const SKILL_META: Record<number, { key: 'Q' | 'W' | 'E' | 'R', fill: string }> = {
  1: { key: 'Q', fill: 'bg-rosegold-200 text-ink-950' },
  2: { key: 'W', fill: 'bg-rosegold-300 text-ink-950' },
  3: { key: 'E', fill: 'bg-rosegold-400 text-ink-950' },
  4: { key: 'R', fill: 'bg-gold text-ink-950' },
}
const rows = computed(() => [1, 2, 3, 4].map((slot) => {
  const meta = SKILL_META[slot]!
  return {
    slot,
    key: meta.key,
    fill: meta.fill,
    spell: championStatic.value?.championSpells[meta.key] ?? null,
    levels: props.skillEvents.map(ev => ev.skillSlot === slot),
  }
}))
</script>

<template>
  <MatchDetailCard title="Skill order">
    <!-- `overflow-y-hidden` is explicit: a lone `overflow-x-auto` computes the
         other axis to `auto` too, so any spill raises a phantom vertical
         scrollbar. The inner block is `w-fit mx-auto` so the grid sits centred
         while it fits and scrolls from its left edge when it doesn't. -->
    <div v-if="skillEvents.length" class="overflow-x-auto overflow-y-hidden">
      <div class="mx-auto w-fit space-y-1">
        <div v-for="row in rows" :key="`skill-${row.slot}`" class="flex items-center gap-1">
          <!-- `flex` on the wrapper removes the icon's inline-baseline descender. -->
          <div class="relative mr-1.5 flex size-7 shrink-0">
            <GameTooltipChampionSpellIcon
              :spell="row.spell"
              :fallback-label="row.key"
              :pending="pending"
              :width="28"
              :height="28"
              class="size-7 rounded"
            />
            <ItemRankBadge :value="row.key" />
          </div>
          <span
            v-for="(filled, lvl) in row.levels"
            :key="`lvl-${lvl}`"
            class="flex size-6 shrink-0 items-center justify-center rounded-md text-[11px] font-extrabold tabular-nums"
            :class="filled ? row.fill : 'bg-accented text-transparent'"
          >
            {{ filled ? lvl + 1 : '' }}
          </span>
        </div>
      </div>
    </div>
    <p v-else class="text-[11px] text-muted">No skill data for this game.</p>
  </MatchDetailCard>
</template>
