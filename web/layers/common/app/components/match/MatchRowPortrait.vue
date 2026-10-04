<script setup lang="ts">
import { getPositionIconUrl } from '#shared/utils/ddragon'
import { POSITION_BY_VALUE } from '#common/utils/positions'

// Champion portrait of a match-history row, badged with the player's role (or
// the champion level when Riot assigned no position). Summoner spells and runes
// used to sit next to it; they now live just left of the item block
// (`MatchRowLoadout`) so the whole loadout — spells, runes, items — reads as
// one continuous strip, matching the scoreboard layout.
defineProps<{
  iconUrl: string | null
  championName: string
  /**
   * The viewing player's assigned role, taken straight from the PUUID-matched
   * self read-model. Null when Riot never assigned one (old rows, non-SR
   * modes) — the level shows instead. Resolved server-side, so it stays
   * correct even in queues that allow duplicate champions on a team.
   */
  position: string | null
  championLevel: number
}>()

// The lane glyph overlaying the portrait renders as a plain <img> (one
// component instance per icon is not worth it at this count), so it needs the
// canonical URL built explicitly — without it the raw `/positions/*.png` was
// served full-size for a 12 px box, and as a fourth distinct cache entry for a
// glyph the rest of the page already had.
const canonicalIcon = useCanonicalIcon()
</script>

<template>
  <div class="relative shrink-0 @2xl:ml-1">
    <SkeletonImage
      :src="iconUrl"
      :alt="championName"
      :title="championName"
      loading="lazy"
      class="size-10 rounded @2xl:size-12"
    />
    <span
      class="absolute -bottom-1 -right-1 inline-flex items-center justify-center rounded-full bg-default ring-1 ring-default"
      :class="position
        ? 'size-4 @2xl:size-5'
        : 'size-3.5 text-[9px] font-bold leading-none @2xl:size-4 @2xl:text-[10px]'"
      :title="position ? (POSITION_BY_VALUE.get(position)?.label ?? position) : undefined"
    >
      <img
        v-if="position"
        :src="canonicalIcon(getPositionIconUrl(position))"
        loading="lazy"
        :alt="POSITION_BY_VALUE.get(position)?.label ?? position"
        class="size-3 @2xl:size-3.5"
      >
      <template v-else>{{ championLevel }}</template>
    </span>
  </div>
</template>
