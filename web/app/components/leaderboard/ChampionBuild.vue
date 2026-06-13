<script setup lang="ts">
import type { LeaderboardTopChampion } from '~~/shared/types/leaderboard'
import type { StaticItemData, StaticPerkData, StaticPerkStyleData } from '~~/shared/types/static-data'

// The "this is what they main" cluster used on the truemains leaderboard and
// the homepage teaser: champion icon + play rate + the player's keystone (with
// secondary tree overlay) + their first item — mirroring how the champion list
// renders a top build. Build ids are resolved to icon objects by the caller
// (via useBuildAssets) so this component stays free of data fetching.
//
// `nameTag` makes the champion icon a link to the player-scoped build page;
// omit it (homepage, where the whole row is already a link) for a plain icon.
const props = withDefaults(defineProps<{
  champion: LeaderboardTopChampion
  name: string
  iconUrl: string | null
  keystone: StaticPerkData | null
  secondaryStyle: StaticPerkStyleData | null
  firstItem: StaticItemData | null
  nameTag?: string | null
  /** Tighter sizing + no "play rate" label, for the narrow homepage panel. */
  compact?: boolean
}>(), {
  nameTag: null,
  compact: false,
})

const playRatePct = computed(() => {
  const rate = props.champion.playRate
  return Number.isFinite(rate) ? Math.round(rate * 100) : null
})

const iconSize = computed(() => (props.compact ? 32 : 36))
const buildSize = computed(() => (props.compact ? 24 : 28))
const championTitle = computed(() => `${props.name} · ${props.champion.games} games`)
</script>

<template>
  <div class="flex items-center gap-2">
    <ChampionLink
      v-if="nameTag"
      :champion-id="champion.championId"
      :name="name"
      :icon-url="iconUrl"
      :name-tag="nameTag"
      :title="championTitle"
      class="rounded-md ring-1 ring-default/40"
      :style="{ width: `${iconSize}px`, height: `${iconSize}px` }"
    />
    <SkeletonImage
      v-else
      :src="iconUrl"
      :alt="name"
      :title="championTitle"
      :width="iconSize"
      :height="iconSize"
      class="shrink-0 rounded-md ring-1 ring-default/40"
      :style="{ width: `${iconSize}px`, height: `${iconSize}px` }"
    />

    <div
      v-if="playRatePct !== null"
      class="flex flex-col leading-none"
    >
      <span
        class="font-semibold tabular-nums"
        :class="compact ? 'text-xs' : 'text-sm'"
      >{{ playRatePct }}%</span>
      <span
        v-if="!compact"
        class="mt-0.5 text-[11px] text-muted"
      >play rate</span>
    </div>

    <!-- Keystone with the secondary tree as a small overlay badge — same
         presentation as the champion list's top-build column. -->
    <div
      v-if="keystone"
      class="relative shrink-0"
      :style="{ width: `${buildSize}px`, height: `${buildSize}px` }"
    >
      <GameTooltipPerkIcon
        :perk="keystone"
        :width="buildSize"
        :height="buildSize"
        class="rounded-full"
        :style="{ width: `${buildSize}px`, height: `${buildSize}px` }"
      />
      <GameTooltipPerkStyleIcon
        v-if="secondaryStyle"
        :style="secondaryStyle"
        :width="compact ? 13 : 15"
        :height="compact ? 13 : 15"
        class="absolute -bottom-1 -right-1.5"
        :class="compact ? 'size-[13px]' : 'size-[15px]'"
      />
    </div>

    <GameTooltipItemIcon
      v-if="firstItem"
      :item="firstItem"
      :width="buildSize"
      :height="buildSize"
      class="shrink-0 rounded"
      :style="{ width: `${buildSize}px`, height: `${buildSize}px` }"
    />
  </div>
</template>
